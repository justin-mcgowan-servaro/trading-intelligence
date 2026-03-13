using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
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
    private readonly IConnectionMultiplexer _redis;

    public MomentumController(
        AppDbContext db,
        SignalAggregatorService aggregator,
        IConnectionMultiplexer redis)
    {
        _db = db;
        _aggregator = aggregator;
        _redis = redis;
    }

    // GET /api/momentum/top
    // Returns top 20 tickers by most recent score
    [HttpGet("top")]
    public async Task<IActionResult> GetTop(
    [FromQuery] int limit = 20,
    CancellationToken cancellationToken = default)
    {
        // Get latest score per ticker using a subquery approach
        var latestIds = await _db.MomentumScores
            .GroupBy(s => s.TickerSymbol)
            .Select(g => g.Max(s => s.Id))
            .ToListAsync(cancellationToken);

        var scores = await _db.MomentumScores
            .Where(s => latestIds.Contains(s.Id))
            .OrderByDescending(s => s.TotalScore)
            .Take(limit)
            .Select(s => new
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
            })
            .ToListAsync(cancellationToken);

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

    // GET /api/momentum/alerts
    // Returns recent high-score alerts for dashboard notification panel
    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts()
    {
        var db = _redis.GetDatabase();
        var alerts = await TelegramAlertService.GetRecentAlertsAsync(db);
        return Ok(alerts);
    }
    // POST /api/momentum/{ticker}/analyze
    // Triggers on-demand GPT-4o analysis for a ticker (uses cached score + live buffer)
    [HttpPost("{ticker}/analyze")]
    public async Task<IActionResult> AnalyzeTicker(
        string ticker,
        CancellationToken cancellationToken = default)
    {
        ticker = ticker.ToUpperInvariant();
    
        // Get latest saved score
        var latestScore = await _db.MomentumScores
            .Where(s => s.TickerSymbol == ticker)
            .OrderByDescending(s => s.ScoredAt)
            .FirstOrDefaultAsync(cancellationToken);
    
        if (latestScore == null)
            return NotFound(new { message = $"No score data found for {ticker}" });
    
        // If AI analysis already exists and is recent (< 30 min), return cached
        if (!string.IsNullOrEmpty(latestScore.AiAnalysis) &&
            latestScore.ScoredAt > DateTime.UtcNow.AddMinutes(-30))
        {
            return Ok(new
            {
                ticker,
                cached = true,
                aiAnalysis = latestScore.AiAnalysis,
                score = latestScore.TotalScore,
                scoredAt = MarketSessionHelper.ToSast(latestScore.ScoredAt)
            });
        }
    
        // Otherwise trigger fresh analysis via Redis (same path as normal scoring)
        var pub = _redis.GetSubscriber();
        await pub.PublishAsync(RedisChannel.Literal("scored-signals"), ticker);
    
        return Accepted(new
        {
            ticker,
            cached = false,
            message = "Analysis triggered — check back in a few seconds",
            currentScore = latestScore.TotalScore
        });
    }

}
