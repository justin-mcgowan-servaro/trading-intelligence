using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingIntelligence.Core.Entities;
using TradingIntelligence.Infrastructure.Data;
using TradingIntelligence.Infrastructure.Helpers;

namespace TradingIntelligence.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WatchlistController : ControllerBase
{
    private readonly AppDbContext _db;

    public WatchlistController(AppDbContext db)
    {
        _db = db;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // GET /api/watchlist
    [HttpGet]
    public async Task<IActionResult> GetWatchlist(
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        var watchlist = await _db.Watchlists
            .Where(w => w.UserId == userId)
            .OrderBy(w => w.TickerSymbol)
            .ToListAsync(cancellationToken);

        // Enrich with latest momentum scores
        var tickers = watchlist.Select(w => w.TickerSymbol).ToList();

        var latestScores = await _db.MomentumScores
            .Where(s => tickers.Contains(s.TickerSymbol))
            .GroupBy(s => s.TickerSymbol)
            .Select(g => g.OrderByDescending(s => s.ScoredAt).First())
            .ToListAsync(cancellationToken);

        var result = watchlist.Select(w =>
        {
            var score = latestScores
                .FirstOrDefault(s => s.TickerSymbol == w.TickerSymbol);
            return new
            {
                w.Id,
                w.TickerSymbol,
                w.AlertThreshold,
                w.AlertEnabled,
                w.AddedAt,
                LatestScore = score == null ? null : new
                {
                    score.TotalScore,
                    TradeBias = score.TradeBias.ToString(),
                    ScoredAtSast = MarketSessionHelper.ToSast(score.ScoredAt)
                }
            };
        });

        return Ok(result);
    }

    // POST /api/watchlist
    [HttpPost]
    public async Task<IActionResult> AddToWatchlist(
        [FromBody] WatchlistRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var ticker = request.TickerSymbol.ToUpperInvariant();

        var exists = await _db.Watchlists
            .AnyAsync(w => w.UserId == userId && w.TickerSymbol == ticker,
                cancellationToken);

        if (exists)
            return Conflict(new { message = $"{ticker} already in watchlist" });

        var entry = new Watchlist
        {
            UserId = userId,
            TickerSymbol = ticker,
            AlertThreshold = request.AlertThreshold,
            AlertEnabled = request.AlertEnabled ?? true,
            AddedAt = DateTime.UtcNow
        };

        _db.Watchlists.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = $"{ticker} added to watchlist", entry });
    }

    // DELETE /api/watchlist/{ticker}
    [HttpDelete("{ticker}")]
    public async Task<IActionResult> RemoveFromWatchlist(
        string ticker,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        ticker = ticker.ToUpperInvariant();

        var entry = await _db.Watchlists
            .FirstOrDefaultAsync(w => w.UserId == userId &&
                w.TickerSymbol == ticker, cancellationToken);

        if (entry == null)
            return NotFound(new { message = $"{ticker} not in watchlist" });

        _db.Watchlists.Remove(entry);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = $"{ticker} removed from watchlist" });
    }
}

public record WatchlistRequest(
    string TickerSymbol,
    decimal? AlertThreshold,
    bool? AlertEnabled);