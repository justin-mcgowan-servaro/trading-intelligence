using Microsoft.EntityFrameworkCore;
using TradingIntelligence.Core.Entities;
using TradingIntelligence.Core.Enums;
using TradingIntelligence.Core.Interfaces;
using TradingIntelligence.Infrastructure.Data;

namespace TradingIntelligence.Infrastructure.Services;

public interface IPaperTradeService
{
    Task TryCreateAutoTradeAsync(MomentumScore score);
    Task EvaluateOpenTradesAsync();
    Task UpdateSignalAccuracyAsync(PaperTrade trade);
}

public class PaperTradeService : IPaperTradeService
{
    private readonly AppDbContext _db;
    private readonly IPolygonPriceService _polygon;
    private readonly IMt5BridgeService _mt5Bridge;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PaperTradeService> _logger;

    public PaperTradeService(
        AppDbContext db,
        IPolygonPriceService polygon,
        IMt5BridgeService mt5Bridge,
        IConfiguration configuration,
        ILogger<PaperTradeService> logger)
    {
        _db = db;
        _polygon = polygon;
        _mt5Bridge = mt5Bridge;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task TryCreateAutoTradeAsync(MomentumScore score)
    {
        // Dedup — one trade per MomentumScoreId
        if (await _db.PaperTrades.AnyAsync(p => p.MomentumScoreId == score.Id))
            return;

        var price = await _polygon.GetLastPriceAsync(score.TickerSymbol);
        if (price is null)
        {
            _logger.LogWarning("PaperTrade skipped — no price for {Ticker}", score.TickerSymbol);
            return;
        }

        var direction = score.TradeBias == TradeBias.Long
            ? TradeDirection.Long
            : TradeDirection.Short;

        var trade = new PaperTrade
        {
            TickerSymbol = score.TickerSymbol,
            MomentumScoreId = score.Id,
            EntryPrice = price.Value,
            Direction = direction,
            TradeBias = score.TradeBias,
            TotalScoreAtEntry = score.TotalScore,
            Notes = score.AiAnalysis?[..Math.Min(500, score.AiAnalysis?.Length ?? 0)],
            OpenedAt = DateTime.UtcNow
        };

        _db.PaperTrades.Add(trade);
        await _db.SaveChangesAsync();

        var lotSize = _configuration.GetValue<decimal?>("Trading:DefaultLotSize") ?? 0.01m;
        var ticket = await _mt5Bridge.PlaceOrderAsync(trade.TickerSymbol, trade.Direction, lotSize);
        if (ticket is not null)
        {
            _db.BrokerTrades.Add(new BrokerTrade
            {
                PaperTradeId = trade.Id,
                Mt5Ticket = ticket.Value,
                Mt5Symbol = trade.TickerSymbol,
                LotSize = lotSize,
                BrokerStatus = BrokerStatus.Pending,
                Direction = trade.Direction,
                OpenedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
        else
        {
            _logger.LogWarning("MT5 order skipped for {Ticker} — bridge unavailable", trade.TickerSymbol);
        }

        _logger.LogInformation("PaperTrade opened: {Ticker} {Direction} @ {Price}",
            trade.TickerSymbol, direction, price);
    }

    public async Task EvaluateOpenTradesAsync()
    {
        var openTrades = await _db.PaperTrades
            .Where(p => p.Status == TradeStatus.Open)
            .ToListAsync();

        foreach (var trade in openTrades)
        {
            // Expire trades older than 72 hours
            if (DateTime.UtcNow - trade.OpenedAt > TimeSpan.FromHours(72))
            {
                trade.Status = TradeStatus.Expired;
                trade.ClosedAt = DateTime.UtcNow;
                trade.Outcome = TradeOutcome.Pending; // no clean outcome
                await UpdateSignalAccuracyAsync(trade);
                continue;
            }

            var currentPrice = await _polygon.GetLastPriceAsync(trade.TickerSymbol);
            if (currentPrice is null) continue;

            // Respect Polygon rate limit — 13s delay is handled inside GetLastPriceAsync
            var pnlPoints = trade.Direction == TradeDirection.Long
                ? currentPrice.Value - trade.EntryPrice
                : trade.EntryPrice - currentPrice.Value;

            var pnlPct = pnlPoints / trade.EntryPrice * 100;

            trade.ExitPrice = currentPrice.Value;
            trade.PnlPoints = pnlPoints;
            trade.PnlPercent = pnlPct;

            // Only close and record outcome at thresholds
            if (pnlPct >= 1m)
            {
                trade.Status = TradeStatus.Closed;
                trade.ClosedAt = DateTime.UtcNow;
                trade.Outcome = TradeOutcome.Win;
                await UpdateSignalAccuracyAsync(trade);
            }
            else if (pnlPct <= -1m)
            {
                trade.Status = TradeStatus.Closed;
                trade.ClosedAt = DateTime.UtcNow;
                trade.Outcome = TradeOutcome.Loss;
                await UpdateSignalAccuracyAsync(trade);
            }
            // else stays Open — P&L updated live but not closed yet
        }

        await _db.SaveChangesAsync();
    }

    public async Task UpdateSignalAccuracyAsync(PaperTrade trade)
    {
        if (trade.Outcome == TradeOutcome.Pending) return;

        var acc = await _db.SignalAccuracies
            .FirstOrDefaultAsync(s => s.TickerSymbol == trade.TickerSymbol)
            ?? new SignalAccuracy { TickerSymbol = trade.TickerSymbol };

        var isNew = acc.Id == 0;

        acc.TotalTrades++;
        if (trade.Outcome == TradeOutcome.Win) acc.Wins++;
        else if (trade.Outcome == TradeOutcome.Loss) acc.Losses++;
        else if (trade.Outcome == TradeOutcome.Breakeven) acc.Breakevens++;

        acc.WinRate = acc.TotalTrades > 0
            ? Math.Round((decimal)acc.Wins / acc.TotalTrades * 100, 2)
            : 0;

        // Rolling average PnL
        acc.AvgPnlPercent = Math.Round(
            ((acc.AvgPnlPercent * (acc.TotalTrades - 1)) + (trade.PnlPercent ?? 0)) / acc.TotalTrades, 2);

        acc.AvgScoreAtEntry = Math.Round(
            ((acc.AvgScoreAtEntry * (acc.TotalTrades - 1)) + trade.TotalScoreAtEntry) / acc.TotalTrades, 2);

        acc.LastUpdatedAt = DateTime.UtcNow;

        if (isNew) _db.SignalAccuracies.Add(acc);
        // SaveChanges called by caller
    }
}
