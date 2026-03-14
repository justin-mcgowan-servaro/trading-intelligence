using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingIntelligence.Core.Enums;
using TradingIntelligence.Core.Interfaces;
using TradingIntelligence.Infrastructure.Data;

namespace TradingIntelligence.Api.Controllers;

[ApiController]
[Route("api/trades")]
[Authorize]
public class TradesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IMt5BridgeService _mt5Bridge;
    private readonly IPaperTradeService _paperTradeService;

    public TradesController(AppDbContext db, IMt5BridgeService mt5Bridge, IPaperTradeService paperTradeService)
    {
        _db = db;
        _mt5Bridge = mt5Bridge;
        _paperTradeService = paperTradeService;
    }

    [HttpGet("paper")]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int size = 20)
    {
        var trades = await _db.PaperTrades
            .OrderByDescending(t => t.OpenedAt)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync();
        return Ok(trades);
    }

    [HttpGet("paper/open")]
    public async Task<IActionResult> GetOpen() =>
        Ok(await _db.PaperTrades.Where(t => t.Status == TradeStatus.Open)
            .OrderByDescending(t => t.OpenedAt).ToListAsync());

    [HttpGet("paper/{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var trade = await _db.PaperTrades.FindAsync(id);
        return trade is null ? NotFound() : Ok(trade);
    }

    [HttpGet("accuracy")]
    public async Task<IActionResult> GetAccuracy() =>
        Ok(await _db.SignalAccuracies.OrderByDescending(s => s.WinRate).ToListAsync());

    [HttpGet("accuracy/{ticker}")]
    public async Task<IActionResult> GetAccuracyByTicker(string ticker)
    {
        var acc = await _db.SignalAccuracies
            .FirstOrDefaultAsync(s => s.TickerSymbol == ticker.ToUpper());
        return acc is null ? NotFound() : Ok(acc);
    }

    [HttpPost("paper/{id:int}/close")]
    public async Task<IActionResult> ManualClose(int id)
    {
        var trade = await _db.PaperTrades.FindAsync(id);
        if (trade is null || trade.Status != TradeStatus.Open) return BadRequest();
        trade.Status = TradeStatus.Closed;
        trade.ClosedAt = DateTime.UtcNow;
        trade.Outcome = TradeOutcome.Breakeven;
        await _paperTradeService.UpdateSignalAccuracyAsync(trade);
        await _db.SaveChangesAsync();
        return Ok(trade);
    }

    [HttpGet("broker")]
    public async Task<IActionResult> GetBrokerTrades([FromQuery] int page = 1, [FromQuery] int size = 20)
    {
        var trades = await _db.BrokerTrades
            .Include(t => t.PaperTrade)
            .OrderByDescending(t => t.OpenedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(t => new
            {
                ticker = t.PaperTrade.TickerSymbol,
                direction = t.Direction,
                lotSize = t.LotSize,
                filledPrice = t.FilledPrice,
                currentPrice = t.CurrentPrice,
                brokerStatus = t.BrokerStatus,
                openedAt = t.OpenedAt,
                closedAt = t.ClosedAt,
                mt5Ticket = t.Mt5Ticket
            })
            .ToListAsync();

        return Ok(trades);
    }

    [HttpGet("broker/open")]
    public async Task<IActionResult> GetOpenBrokerTrades() =>
        Ok(await _db.BrokerTrades
            .Include(t => t.PaperTrade)
            .Where(t => t.BrokerStatus == BrokerStatus.Pending || t.BrokerStatus == BrokerStatus.Filled)
            .OrderByDescending(t => t.OpenedAt)
            .ToListAsync());

    [HttpPost("broker/{id:int}/close")]
    public async Task<IActionResult> CloseBrokerTrade(int id)
    {
        var trade = await _db.BrokerTrades.FindAsync(id);
        if (trade is null)
            return BadRequest(new { message = "Broker trade not found" });

        if (trade.BrokerStatus == BrokerStatus.Closed || trade.BrokerStatus == BrokerStatus.Failed)
            return BadRequest(new { message = "Broker trade cannot be closed" });

        var closed = await _mt5Bridge.CloseOrderAsync(trade.Mt5Ticket);
        if (!closed)
            return StatusCode(500, new { message = "MT5 close failed" });

        trade.BrokerStatus = BrokerStatus.Closed;
        trade.ClosedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(trade);
    }
}
