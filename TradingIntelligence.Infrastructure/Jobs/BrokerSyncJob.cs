using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quartz;
using TradingIntelligence.Core.Entities;
using TradingIntelligence.Core.Enums;
using TradingIntelligence.Core.Interfaces;
using TradingIntelligence.Infrastructure.Data;
using TradingIntelligence.Infrastructure.Services;

namespace TradingIntelligence.Infrastructure.Jobs;

public class BrokerSyncJob : IJob
{
    private readonly AppDbContext _db;
    private readonly IMt5BridgeService _mt5Bridge;
    private readonly IPaperTradeService _paperTradeService;
    private readonly ILogger<BrokerSyncJob> _logger;

    private static readonly SemaphoreSlim _throttle = new(1, 1);
    private static DateTime _lastCall = DateTime.MinValue;
    private const int DelayMs = 13000;

    public BrokerSyncJob(
        AppDbContext db,
        IMt5BridgeService mt5Bridge,
        IPaperTradeService paperTradeService,
        ILogger<BrokerSyncJob> logger)
    {
        _db = db;
        _mt5Bridge = mt5Bridge;
        _paperTradeService = paperTradeService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var trades = await _db.BrokerTrades
            .Include(t => t.PaperTrade)
            .Where(t => t.BrokerStatus == BrokerStatus.Pending || t.BrokerStatus == BrokerStatus.Filled)
            .OrderBy(t => t.OpenedAt)
            .ToListAsync(context.CancellationToken);

        foreach (var trade in trades)
        {
            var position = await GetPositionWithThrottleAsync(trade.Mt5Ticket, context.CancellationToken);
            if (position is null)
            {
                var retries = 0;
                if (!string.IsNullOrWhiteSpace(trade.ErrorMessage) && trade.ErrorMessage.StartsWith("SyncFailed:"))
                {
                    int.TryParse(trade.ErrorMessage.Split(':').LastOrDefault(), out retries);
                }

                retries++;
                trade.ErrorMessage = $"SyncFailed:{retries}";
                continue;
            }

            trade.SyncedAt = DateTime.UtcNow;
            trade.CurrentPrice = position.CurrentPrice;
            trade.ErrorMessage = null;

            if (position.IsOpen)
            {
                trade.BrokerStatus = BrokerStatus.Filled;
                if (trade.FilledPrice is null && position.CurrentPrice > 0)
                    trade.FilledPrice = position.CurrentPrice;
                continue;
            }

            trade.BrokerStatus = BrokerStatus.Closed;
            trade.ClosedAt = DateTime.UtcNow;

            var paperTrade = trade.PaperTrade;
            if (paperTrade.Status == TradeStatus.Closed || paperTrade.Status == TradeStatus.Expired)
                continue;

            var exitPrice = position.CurrentPrice;
            var pnlPoints = paperTrade.Direction == TradeDirection.Long
                ? exitPrice - paperTrade.EntryPrice
                : paperTrade.EntryPrice - exitPrice;
            var pnlPercent = paperTrade.EntryPrice == 0
                ? 0
                : pnlPoints / paperTrade.EntryPrice * 100;

            paperTrade.ExitPrice = exitPrice;
            paperTrade.PnlPoints = pnlPoints;
            paperTrade.PnlPercent = pnlPercent;
            paperTrade.Status = TradeStatus.Closed;
            paperTrade.ClosedAt = DateTime.UtcNow;

            if (pnlPercent >= 1m) paperTrade.Outcome = TradeOutcome.Win;
            else if (pnlPercent <= -1m) paperTrade.Outcome = TradeOutcome.Loss;
            else paperTrade.Outcome = TradeOutcome.Breakeven;

            await _paperTradeService.UpdateSignalAccuracyAsync(paperTrade);
        }

        await _db.SaveChangesAsync(context.CancellationToken);
        _logger.LogInformation("Broker sync job processed {Count} broker trades", trades.Count);
    }

    private async Task<TradingIntelligence.Core.Models.Mt5PositionResult?> GetPositionWithThrottleAsync(
        long ticket,
        CancellationToken cancellationToken)
    {
        await _throttle.WaitAsync(cancellationToken);
        try
        {
            var wait = DelayMs - (int)(DateTime.UtcNow - _lastCall).TotalMilliseconds;
            if (wait > 0) await Task.Delay(wait, cancellationToken);

            var result = await _mt5Bridge.GetPositionAsync(ticket);
            _lastCall = DateTime.UtcNow;
            return result;
        }
        finally
        {
            _throttle.Release();
        }
    }
}
