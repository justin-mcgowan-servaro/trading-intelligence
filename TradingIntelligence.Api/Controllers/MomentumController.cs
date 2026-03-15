using System.Text.Json;
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
    private const string AnalysisJobPrefix = "analysis-job";
    private static readonly TimeSpan AnalysisJobTtl = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _db;
    private readonly SignalAggregatorService _aggregator;
    private readonly IConnectionMultiplexer _redis;
    private readonly MomentumScoringService _momentumScoringService;

    public MomentumController(
        AppDbContext db,
        SignalAggregatorService aggregator,
        IConnectionMultiplexer redis,
        MomentumScoringService momentumScoringService)
    {
        _db = db;
        _aggregator = aggregator;
        _redis = redis;
        _momentumScoringService = momentumScoringService;
    }

    // GET /api/momentum/top
    // Returns top 20 tickers by most recent score
    [HttpGet("top")]
    public async Task<IActionResult> GetTop(
    [FromQuery] int limit = 20,
    CancellationToken cancellationToken = default)
    {
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
    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts()
    {
        var db = _redis.GetDatabase();
        var alerts = await TelegramAlertService.GetRecentAlertsAsync(db);
        return Ok(alerts);
    }

    // POST /api/momentum/{ticker}/analyze
    [HttpPost("{ticker}/analyze")]
    public async Task<IActionResult> AnalyzeTicker(
        string ticker,
        CancellationToken cancellationToken = default)
    {
        ticker = ticker.ToUpperInvariant();

        var latestScore = await _db.MomentumScores
            .Where(s => s.TickerSymbol == ticker)
            .OrderByDescending(s => s.ScoredAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestScore == null)
            return NotFound(new { message = $"No score data found for {ticker}" });

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

        var jobId = Guid.NewGuid().ToString("N");
        var state = new AnalysisJobState(
            JobId: jobId,
            TickerSymbol: ticker,
            BaselineScoreId: latestScore.Id,
            TriggeredAtUtc: DateTime.UtcNow,
            Status: "processing");

        await SaveAnalysisJobStateAsync(state);

        _ = Task.Run(async () =>
        {
            var result = await _momentumScoringService.GenerateManualAnalysisAsync(
                ticker,
                latestScore.Id,
                CancellationToken.None);

            var updatedState = state with
            {
                Status = result.Status,
                Reason = result.Reason,
                ScoreId = result.ScoreId,
                CompletedAtUtc = result.CompletedAtUtc
            };

            await SaveAnalysisJobStateAsync(updatedState);
        });

        return Accepted(new
        {
            ticker,
            cached = false,
            analysisJobId = jobId,
            message = "Analysis triggered — check back in a few seconds",
            currentScore = latestScore.TotalScore
        });
    }

    // GET /api/momentum/analysis-jobs/{jobId}
    [HttpGet("analysis-jobs/{jobId}")]
    public async Task<IActionResult> GetAnalysisJobStatus(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var raw = await db.StringGetAsync(BuildAnalysisJobKey(jobId));
        if (raw.IsNullOrEmpty)
            return NotFound(new { message = "Analysis job not found or expired" });

        var state = JsonSerializer.Deserialize<AnalysisJobState>(raw!);
        if (state == null)
            return NotFound(new { message = "Analysis job state is invalid" });

        var latestScore = await _db.MomentumScores
            .Where(s => s.TickerSymbol == state.TickerSymbol)
            .OrderByDescending(s => s.ScoredAt)
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(new
        {
            jobId = state.JobId,
            ticker = state.TickerSymbol,
            status = state.Status,
            reason = state.Reason,
            hasAnalysis = !string.IsNullOrWhiteSpace(latestScore?.AiAnalysis),
            scoredAt = latestScore == null
                ? null
                : MarketSessionHelper.ToSast(latestScore.ScoredAt),
            completedAt = state.CompletedAtUtc == null
                ? null
                : MarketSessionHelper.ToSast(state.CompletedAtUtc.Value)
        });
    }

    // GET /api/momentum/history
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(CancellationToken cancellationToken)
    {
        var scores = await _db.MomentumScores
            .AsNoTracking()
            .Select(s => new
            {
                s.TickerSymbol,
                s.TotalScore,
                s.ScoredAt
            })
            .ToListAsync(cancellationToken);

        var grouped = scores
            .GroupBy(s => s.TickerSymbol)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.ScoredAt)
                      .Take(20)
                      .OrderBy(x => x.ScoredAt)
                      .Select(x => (double)x.TotalScore)
                      .ToList());

        return Ok(grouped);
    }

    private async Task SaveAnalysisJobStateAsync(AnalysisJobState state)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync(
            BuildAnalysisJobKey(state.JobId),
            JsonSerializer.Serialize(state),
            AnalysisJobTtl);
    }

    private static string BuildAnalysisJobKey(string jobId)
        => $"{AnalysisJobPrefix}:{jobId}";

    private sealed record AnalysisJobState(
        string JobId,
        string TickerSymbol,
        int BaselineScoreId,
        DateTime TriggeredAtUtc,
        string Status,
        string? Reason = null,
        int? ScoreId = null,
        DateTime? CompletedAtUtc = null);
}
