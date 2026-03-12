using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingIntelligence.Infrastructure.Data;
using TradingIntelligence.Infrastructure.Helpers;
using TradingIntelligence.Infrastructure.Services;

namespace TradingIntelligence.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MomentumController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly SignalAggregatorService _aggregator;

    public MomentumController(
        AppDbContext db,
        SignalAggregatorService aggregator)
    {
        _db = db;
        _aggregator = aggregator;
    }

    // GET /api/momentum/top
    // Returns top 20 tickers by most recent score
    [HttpGet("top")]
    public async Task<IActionResult> GetTop(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var data = await _db.MomentumScores
    .GroupBy(s => s.TickerSymbol)
    .Select(g => g.OrderByDescending(s => s.ScoredAt).First())
    .OrderByDescending(s => s.TotalScore)
    .Take(limit)
    .ToListAsync(cancellationToken);

        var scores = data.Select(s => new
        {
            s.TickerSymbol,
            s.TotalScore,
            s.RedditScore,
            s.NewsScore,
            s.VolumeScore,
            s.OptionsScore,
            s.SentimentScore,
            TradeBias = s.TradeBias.ToString(),
            Session = s.Session.ToString(),
            ScoredAtSast = MarketSessionHelper.ToSast(s.ScoredAt),
            ScoredAt = s.ScoredAt,
            HasAiAnalysis = !string.IsNullOrEmpty(s.AiAnalysis)
        }).ToList();

        return Ok(new
        {
            count = scores.Count,
            generatedAt = MarketSessionHelper.ToSast(DateTime.UtcNow),
            session = MarketSessionHelper.SessionDisplayName(
                MarketSessionHelper.CurrentSession()),
            data = scores
        });
    }

    // GET /api/momentum/{ticker}
    // Returns full history + latest AI analysis for a ticker
    [HttpGet("{ticker}")]
    public async Task<IActionResult> GetTicker(
        string ticker,
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        ticker = ticker.ToUpperInvariant();

        var scores = await _db.MomentumScores
            .Where(s => s.TickerSymbol == ticker)
            .OrderByDescending(s => s.ScoredAt)
            .Take(limit)
            .Select(s => new
            {
                s.Id,
                s.TickerSymbol,
                s.TotalScore,
                s.RedditScore,
                s.NewsScore,
                s.VolumeScore,
                s.OptionsScore,
                s.SentimentScore,
                TradeBias = s.TradeBias.ToString(),
                Session = s.Session.ToString(),
                s.SignalSummary,
                s.AiAnalysis,
                s.TradeSetup,
                s.RiskFactors,
                ScoredAtSast = MarketSessionHelper.ToSast(s.ScoredAt),
                ScoredAt = s.ScoredAt
            })
            .ToListAsync(cancellationToken);

        if (!scores.Any())
            return NotFound(new { message = $"No scores found for {ticker}" });

        // Get current buffer status
        var bufferSignals = _aggregator.GetTickerSignals(ticker);

        return Ok(new
        {
            ticker,
            latest = scores.First(),
            history = scores,
            currentBuffer = new
            {
                signalCount = bufferSignals.Count,
                signalTypes = bufferSignals
                    .Select(s => s.SignalType.ToString())
                    .Distinct()
            }
        });
    }

    // GET /api/momentum/buffer
    // Returns current in-memory signal buffer summary
    [HttpGet("buffer")]
    public IActionResult GetBuffer()
    {
        var summary = _aggregator.GetBufferSummary();
        return Ok(new
        {
            generatedAt = MarketSessionHelper.ToSast(DateTime.UtcNow),
            session = MarketSessionHelper.SessionDisplayName(
                MarketSessionHelper.CurrentSession()),
            tickers = summary.OrderByDescending(kv => kv.Value)
                .Select(kv => new { ticker = kv.Key, signalCount = kv.Value })
        });
    }
}