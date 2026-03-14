using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using TradingIntelligence.Core.Interfaces;
using TradingIntelligence.Core.Models;
using TradingIntelligence.Infrastructure.Data;
using TradingIntelligence.Infrastructure.Helpers;
using TradingIntelligence.Infrastructure.Services;

namespace TradingIntelligence.Api.Controllers;

[ApiController]
[Route("api/dev")]
public class DevToolsController : ControllerBase
{
    private const string ScoredSignalsChannel = "scored-signals";

    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly TelegramAlertService _telegramAlertService;
    private readonly IPaperTradeService _paperTradeService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DevToolsController> _logger;

    public DevToolsController(
        AppDbContext db,
        IConnectionMultiplexer redis,
        TelegramAlertService telegramAlertService,
        IPaperTradeService paperTradeService,
        IWebHostEnvironment environment,
        ILogger<DevToolsController> logger)
    {
        _db = db;
        _redis = redis;
        _telegramAlertService = telegramAlertService;
        _paperTradeService = paperTradeService;
        _environment = environment;
        _logger = logger;
    }

    // Dev-only controller actions for local replay/testing workflows.
    [HttpPost("trigger-score/{ticker}")]
    public async Task<IActionResult> TriggerScore(
        string ticker,
        CancellationToken cancellationToken = default)
    {
        if (!TryEnsureDevelopment(out var guardResult))
            return guardResult;

        var normalizedTicker = ticker.ToUpperInvariant();

        var subscriber = _redis.GetSubscriber();
        await subscriber.PublishAsync(
            RedisChannel.Literal(ScoredSignalsChannel),
            normalizedTicker,
            CommandFlags.None);

        _logger.LogInformation(
            "Dev trigger-score published {Ticker} to {Channel}",
            normalizedTicker,
            ScoredSignalsChannel);

        return Accepted(new
        {
            ticker = normalizedTicker,
            published = true,
            channel = ScoredSignalsChannel,
            environment = _environment.EnvironmentName
        });
    }

    [HttpPost("replay-latest-alert/{ticker}")]
    public async Task<IActionResult> ReplayLatestAlert(
        string ticker,
        CancellationToken cancellationToken = default)
    {
        if (!TryEnsureDevelopment(out var guardResult))
            return guardResult;

        var normalizedTicker = ticker.ToUpperInvariant();

        var latestScore = await GetLatestScoreAsync(normalizedTicker, cancellationToken);
        if (latestScore == null)
            return NotFound(new { message = $"No score found for {normalizedTicker}" });

        var result = MapToMomentumScoreResult(latestScore);
        await _telegramAlertService.SendScoreAlertAsync(result, cancellationToken);

        return Ok(new
        {
            ticker = normalizedTicker,
            replayed = true,
            scoreId = latestScore.Id,
            totalScore = latestScore.TotalScore,
            attempted = "telegram"
        });
    }

    [HttpPost("replay-latest-papertrade/{ticker}")]
    public async Task<IActionResult> ReplayLatestPaperTrade(
        string ticker,
        CancellationToken cancellationToken = default)
    {
        if (!TryEnsureDevelopment(out var guardResult))
            return guardResult;

        var normalizedTicker = ticker.ToUpperInvariant();

        var latestScore = await GetLatestScoreAsync(normalizedTicker, cancellationToken);
        if (latestScore == null)
            return NotFound(new { message = $"No score found for {normalizedTicker}" });

        await _paperTradeService.TryCreateAutoTradeAsync(latestScore);

        return Ok(new
        {
            ticker = normalizedTicker,
            replayed = true,
            scoreId = latestScore.Id,
            attempted = "papertrade"
        });
    }

    [HttpGet("latest-score/{ticker}")]
    public async Task<IActionResult> GetLatestScore(
        string ticker,
        CancellationToken cancellationToken = default)
    {
        if (!TryEnsureDevelopment(out var guardResult))
            return guardResult;

        var normalizedTicker = ticker.ToUpperInvariant();

        var latestScore = await GetLatestScoreAsync(normalizedTicker, cancellationToken);
        if (latestScore == null)
            return NotFound(new { message = $"No score found for {normalizedTicker}" });

        return Ok(new
        {
            id = latestScore.Id,
            tickerSymbol = latestScore.TickerSymbol,
            totalScore = latestScore.TotalScore,
            redditScore = latestScore.RedditScore,
            newsScore = latestScore.NewsScore,
            volumeScore = latestScore.VolumeScore,
            optionsScore = latestScore.OptionsScore,
            sentimentScore = latestScore.SentimentScore,
            tradeBias = latestScore.TradeBias.ToString(),
            session = latestScore.Session.ToString(),
            aiAnalysisPresent = !string.IsNullOrWhiteSpace(latestScore.AiAnalysis),
            scoredAt = latestScore.ScoredAt
        });
    }

    private bool TryEnsureDevelopment(out IActionResult guardResult)
    {
        if (_environment.IsDevelopment())
        {
            guardResult = null!;
            return true;
        }

        _logger.LogWarning(
            "Blocked dev-only endpoint access in environment {Environment}",
            _environment.EnvironmentName);
        guardResult = NotFound();
        return false;
    }

    private Task<Core.Entities.MomentumScore?> GetLatestScoreAsync(
        string ticker,
        CancellationToken cancellationToken)
    {
        return _db.MomentumScores
            .Where(s => s.TickerSymbol == ticker)
            .OrderByDescending(s => s.ScoredAt)
            .ThenByDescending(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static MomentumScoreResult MapToMomentumScoreResult(
        Core.Entities.MomentumScore score)
    {
        return new MomentumScoreResult
        {
            TickerSymbol = score.TickerSymbol,
            TotalScore = score.TotalScore,
            RedditScore = score.RedditScore,
            NewsScore = score.NewsScore,
            VolumeScore = score.VolumeScore,
            OptionsScore = score.OptionsScore,
            SentimentScore = score.SentimentScore,
            TradeBias = score.TradeBias,
            SignalSummary = score.SignalSummary ?? string.Empty,
            AiAnalysis = score.AiAnalysis ?? string.Empty,
            Session = score.Session,
            ScoredAt = score.ScoredAt,
            ScoredAtSast = MarketSessionHelper.ToSast(score.ScoredAt),
            Confidence = score.TotalScore >= 80 ? "HIGH"
                : score.TotalScore >= 60 ? "MEDIUM" : "LOW"
        };
    }
}
