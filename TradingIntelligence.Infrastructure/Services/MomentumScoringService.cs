using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using TradingIntelligence.Core.Enums;
using TradingIntelligence.Core.Interfaces;
using TradingIntelligence.Core.Models;
using TradingIntelligence.Core.Prompts;
using TradingIntelligence.Infrastructure.Data;
using TradingIntelligence.Infrastructure.Helpers;

namespace TradingIntelligence.Infrastructure.Services;

public class MomentumScoringService : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SignalAggregatorService _aggregator;
    private readonly IConfiguration _config;
    private readonly ILogger<MomentumScoringService> _logger;

    private readonly IRealtimeNotifier _notifier;

    private const decimal MinScoreForAi = 60m;
    private readonly TelegramAlertService _telegram;

    public MomentumScoringService(
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory,
        SignalAggregatorService aggregator,
        IConfiguration config,
        ILogger<MomentumScoringService> logger,
        IRealtimeNotifier notifier,
        TelegramAlertService telegram)
    {
        _redis = redis;
        _scopeFactory = scopeFactory;
        _aggregator = aggregator;
        _config = config;
        _logger = logger;
        _notifier = notifier;
        _telegram = telegram;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MomentumScoringService starting — subscribing to scored-signals");

        var subscriber = _redis.GetSubscriber();

        await subscriber.SubscribeAsync(
            RedisChannel.Literal("scored-signals"),
            async (channel, message) =>
            {
                if (message.IsNullOrEmpty) return;
                var ticker = message.ToString();
                await ScoreTickerAsync(ticker, stoppingToken);
            });

        _logger.LogInformation("Subscribed to Redis channel: scored-signals");

        // Keep alive
        while (!stoppingToken.IsCancellationRequested)
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
    }

    private async Task ScoreTickerAsync(
        string ticker,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Scoring ticker {Ticker} at {Time}",
                ticker, MarketSessionHelper.ToSast(DateTime.UtcNow));

            // Get all signals for this ticker from the aggregator buffer
            var signals = _aggregator.GetTickerSignals(ticker);
            if (!signals.Any())
            {
                _logger.LogWarning("No signals found for {Ticker}", ticker);
                return;
            }

            // ── Calculate component scores ───────────────────────────────
            var redditScore = CalculateRedditScore(signals);
            var newsScore = CalculateNewsScore(signals);
            var volumeScore = CalculateVolumeScore(signals);
            var optionsScore = 0m; // Options collector added in Session 5
            var sentimentScore = CalculateSentimentScore(signals);

            var totalScore = redditScore + newsScore + volumeScore
                           + optionsScore + sentimentScore;

            var session = MarketSessionHelper.CurrentSession();

            _logger.LogInformation(
                "{Ticker} scored {Total}/100 — R:{Reddit} N:{News} V:{Vol} S:{Sent} | {Session}",
                ticker, totalScore, redditScore, newsScore,
                volumeScore, sentimentScore,
                MarketSessionHelper.SessionDisplayName(session));

            // ── Determine trade bias from signal direction ───────────────
            var tradeBias = DetermineTradeBias(signals, totalScore);

            // ── Call OpenAI only if score qualifies ──────────────────────
            string aiAnalysis = string.Empty;

            if (totalScore >= MinScoreForAi)
            {
                _logger.LogInformation(
                    "{Ticker} qualifies for AI analysis (score {Score})",
                    ticker, totalScore);

                aiAnalysis = await CallOpenAiAsync(
                    ticker, signals,
                    redditScore, newsScore, volumeScore, optionsScore, sentimentScore,
                    totalScore, session,
                    cancellationToken);
            }

            // ── Persist to PostgreSQL ────────────────────────────────────
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var scoreEntity = new Core.Entities.MomentumScore
            {
                TickerSymbol = ticker,
                TotalScore = totalScore,
                RedditScore = redditScore,
                NewsScore = newsScore,
                VolumeScore = volumeScore,
                OptionsScore = optionsScore,
                SentimentScore = sentimentScore,
                TradeBias = tradeBias,
                SignalSummary = BuildSignalSummary(signals),
                AiAnalysis = aiAnalysis,
                Session = session,
                ScoredAt = DateTime.UtcNow
            };

            db.MomentumScores.Add(scoreEntity);
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Saved momentum score for {Ticker} — {Score}/100 {Bias} | SAST: {Time}",
                ticker, totalScore, tradeBias,
                MarketSessionHelper.ToSast(DateTime.UtcNow));
            // Auto paper trade — fires at 65+, only Long or Short bias
            if (totalScore >= 65 && (tradeBias == TradeBias.Long || tradeBias == TradeBias.Short))
            {
                var paperTradeService = scope.ServiceProvider
                    .GetRequiredService<IPaperTradeService>();
                await paperTradeService.TryCreateAutoTradeAsync(scoreEntity);
            }
            // ── Publish result to scored-results channel for SignalR ─────
            var pub = _redis.GetSubscriber();
            await pub.PublishAsync(
                RedisChannel.Literal("scored-results"),
                JsonSerializer.Serialize(new MomentumScoreResult
                {
                    TickerSymbol = ticker,
                    TotalScore = totalScore,
                    RedditScore = redditScore,
                    NewsScore = newsScore,
                    VolumeScore = volumeScore,
                    OptionsScore = optionsScore,
                    SentimentScore = sentimentScore,
                    TradeBias = tradeBias,
                    Confidence = totalScore >= 80 ? "HIGH"
                                   : totalScore >= 60 ? "MEDIUM" : "LOW",
                    SignalSummary = BuildSignalSummary(signals),
                    AiAnalysis = aiAnalysis,
                    Session = session,
                    ScoredAt = DateTime.UtcNow,
                    ScoredAtSast = MarketSessionHelper.ToSast(DateTime.UtcNow)
                }));

            // ── Push to Angular via SignalR ──────────────────────────────────────
            await _notifier.NotifyMomentumUpdate(new MomentumScoreResult
            {
                TickerSymbol = ticker,
                TotalScore = totalScore,
                RedditScore = redditScore,
                NewsScore = newsScore,
                VolumeScore = volumeScore,
                OptionsScore = optionsScore,
                SentimentScore = sentimentScore,
                TradeBias = tradeBias,
                Confidence = totalScore >= 80 ? "HIGH"
                               : totalScore >= 60 ? "MEDIUM" : "LOW",
                SignalSummary = BuildSignalSummary(signals),
                AiAnalysis = aiAnalysis,
                Session = session,
                ScoredAt = DateTime.UtcNow,
                ScoredAtSast = MarketSessionHelper.ToSast(DateTime.UtcNow)
            }, cancellationToken);
            // ── Send Telegram alert if score qualifies ───────────────────────────
            await _telegram.SendScoreAlertAsync(new MomentumScoreResult
            {
                TickerSymbol = ticker,
                TotalScore = totalScore,
                RedditScore = redditScore,
                NewsScore = newsScore,
                VolumeScore = volumeScore,
                OptionsScore = optionsScore,
                SentimentScore = sentimentScore,
                TradeBias = tradeBias,
                Confidence = totalScore >= 80 ? "HIGH"
                               : totalScore >= 60 ? "MEDIUM" : "LOW",
                SignalSummary = BuildSignalSummary(signals),
                AiAnalysis = aiAnalysis,
                Session = session,
                ScoredAt = DateTime.UtcNow,
                ScoredAtSast = MarketSessionHelper.ToSast(DateTime.UtcNow)
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scoring ticker {Ticker}", ticker);
        }
    }

    // ── Scoring calculators ──────────────────────────────────────────────────

    private static decimal CalculateRedditScore(List<RawSignalEvent> signals)
    {
        var redditSignals = signals
            .Where(s => s.SignalType == SignalType.RedditMomentum)
            .ToList();

        if (!redditSignals.Any()) return 0;

        int count = redditSignals.Count;
        decimal avgSentiment = redditSignals.Average(s => s.SentimentScore);

        // Base score from mention count
        decimal baseScore = count switch
        {
            >= 20 => 15,
            >= 10 => 10,
            >= 5 => 7,
            >= 2 => 4,
            _ => 2
        };

        // Sentiment bonus/penalty
        decimal sentimentBonus = avgSentiment switch
        {
            > 0.5m => 5,
            > 0.3m => 3,
            > 0.1m => 1,
            < -0.3m => -3,
            //< -0.5m => -5,
            _ => 0
        };

        return Math.Min(20, Math.Max(0, baseScore + sentimentBonus));
    }

    private static decimal CalculateNewsScore(List<RawSignalEvent> signals)
    {
        var newsSignals = signals
            .Where(s => s.SignalType == SignalType.NewsCatalyst)
            .ToList();

        if (!newsSignals.Any()) return 0;

        // Use the highest catalyst score found in raw data
        decimal maxScore = 0;

        foreach (var signal in newsSignals)
        {
            try
            {
                if (string.IsNullOrEmpty(signal.RawData)) continue;
                var data = JsonSerializer.Deserialize<JsonElement>(signal.RawData);

                if (data.TryGetProperty("CatalystScore", out var scoreEl))
                {
                    var score = scoreEl.GetDecimal();
                    if (score > maxScore) maxScore = score;
                }
            }
            catch { /* ignore parse errors */ }
        }

        // Multiple news sources boost the score
        if (newsSignals.Count >= 3) maxScore = Math.Min(20, maxScore + 3);
        else if (newsSignals.Count >= 2) maxScore = Math.Min(20, maxScore + 1);

        return Math.Min(20, maxScore);
    }

    private static decimal CalculateVolumeScore(List<RawSignalEvent> signals)
    {
        var volumeSignals = signals
            .Where(s => s.SignalType == SignalType.VolumeSpike)
            .ToList();

        if (!volumeSignals.Any()) return 0;

        decimal maxRatio = 0;

        foreach (var signal in volumeSignals)
        {
            try
            {
                if (string.IsNullOrEmpty(signal.RawData)) continue;
                var data = JsonSerializer.Deserialize<JsonElement>(signal.RawData);

                if (data.TryGetProperty("VolumeRatio", out var ratioEl))
                {
                    var ratio = ratioEl.GetDecimal();
                    if (ratio > maxRatio) maxRatio = ratio;
                }
            }
            catch { /* ignore */ }
        }

        return maxRatio switch
        {
            >= 3.0m => 20,
            >= 2.5m => 16,
            >= 2.0m => 12,
            >= 1.5m => 8,
            >= 1.3m => 5,
            _ => 0
        };
    }

    private static decimal CalculateSentimentScore(List<RawSignalEvent> signals)
    {
        var sentimentSignals = signals
            .Where(s => s.SignalType == SignalType.SocialSentiment)
            .ToList();

        if (!sentimentSignals.Any()) return 0;

        decimal avgSentiment = sentimentSignals.Average(s => s.SentimentScore);

        // Check velocity from raw data
        decimal velocityBonus = 0;
        foreach (var signal in sentimentSignals)
        {
            try
            {
                if (string.IsNullOrEmpty(signal.RawData)) continue;
                var data = JsonSerializer.Deserialize<JsonElement>(signal.RawData);

                if (data.TryGetProperty("VelocityPercent", out var velEl))
                {
                    var velocity = velEl.GetDecimal();
                    if (velocity > 50) velocityBonus = 5;
                    else if (velocity > 25) velocityBonus = 3;
                }
            }
            catch { /* ignore */ }
        }

        decimal baseScore = avgSentiment switch
        {
            > 0.6m => 15,
            > 0.3m => 10,
            > 0.1m => 7,
            > -0.1m => 4,
            > -0.3m => 2,
            _ => 0
        };

        return Math.Min(20, baseScore + velocityBonus);
    }

    private static TradeBias DetermineTradeBias(
        List<RawSignalEvent> signals,
        decimal totalScore)
    {
        if (totalScore < 40) return TradeBias.NoTrade;

        var avgSentiment = signals.Any()
            ? signals.Average(s => s.SentimentScore)
            : 0;

        // Check for conflicting signals
        var bullishCount = signals.Count(s => s.SentimentScore > 0.2m);
        var bearishCount = signals.Count(s => s.SentimentScore < -0.2m);
        bool conflicting = bullishCount > 0 && bearishCount > 0 &&
                           Math.Abs(bullishCount - bearishCount) <= 1;

        if (conflicting) return TradeBias.Watch;
        if (totalScore < 60) return TradeBias.Watch;

        return avgSentiment switch
        {
            > 0.2m => TradeBias.Long,
            < -0.2m => TradeBias.Short,
            _ => TradeBias.Watch
        };
    }

    private static string BuildSignalSummary(List<RawSignalEvent> signals)
    {
        var parts = signals
            .GroupBy(s => s.SignalType)
            .Select(g => $"{g.Key}: {g.Count()} signals");

        return string.Join(" | ", parts);
    }

    private async Task<string> CallOpenAiAsync(
        string ticker,
        List<RawSignalEvent> signals,
        decimal redditScore, decimal newsScore, decimal volumeScore,
        decimal optionsScore, decimal sentimentScore,
        decimal totalScore, MarketSession session,
        CancellationToken cancellationToken)
    {
        var apiKey = _config["OpenAI:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("OpenAI API key not configured");
            return string.Empty;
        }

        try
        {
            // Build the data payload for OpenAI
            var dataPayload = BuildOpenAiPayload(
                ticker, signals,
                redditScore, newsScore, volumeScore,
                optionsScore, sentimentScore,
                totalScore, session);

            var client = new ChatClient(
                model: _config["OpenAI:Model"] ?? "gpt-5-nano",
                apiKey: apiKey);

            var messages = new List<ChatMessage>
                {
                    ChatMessage.CreateSystemMessage(SystemPrompts.TradingIntelligence),
                    ChatMessage.CreateUserMessage(dataPayload)
                };

                            var completion = await client.CompleteChatAsync(
                                messages,
                                cancellationToken: cancellationToken);

                            var result = completion.Value.Content[0].Text;

            _logger.LogInformation(
                "OpenAI analysis complete for {Ticker} — {Length} chars",
                ticker, result.Length);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI call failed for {Ticker}", ticker);
            return string.Empty;
        }
    }

    private static string BuildOpenAiPayload(
        string ticker,
        List<RawSignalEvent> signals,
        decimal redditScore, decimal newsScore, decimal volumeScore,
        decimal optionsScore, decimal sentimentScore,
        decimal totalScore, MarketSession session)
    {
        var redditSignals = signals
            .Where(s => s.SignalType == SignalType.RedditMomentum)
            .Select(s => s.RawText)
            .Take(3);

        var newsSignals = signals
            .Where(s => s.SignalType == SignalType.NewsCatalyst)
            .Select(s => s.RawText)
            .Take(3);

        var volumeData = signals
            .Where(s => s.SignalType == SignalType.VolumeSpike)
            .Select(s => s.RawText)
            .FirstOrDefault() ?? "No volume data";

        var sentimentData = signals
            .Where(s => s.SignalType == SignalType.SocialSentiment)
            .Select(s => s.RawText)
            .FirstOrDefault() ?? "No sentiment data";

        return $"""
            TRADING INTELLIGENCE REQUEST — {MarketSessionHelper.ToSast(DateTime.UtcNow)}

            TICKER: {ticker}
            MOMENTUM_SCORE: {totalScore}/100
            ANALYSIS_TIME_SAST: {MarketSessionHelper.ToSast(DateTime.UtcNow)}
            ANALYSIS_TIME_EST: {MarketSessionHelper.ToEst(DateTime.UtcNow)}
            MARKET_SESSION: {MarketSessionHelper.SessionDisplayName(session)}

            SIGNAL_SCORES:
              reddit_score:    {redditScore}/20   reddit_data:    {string.Join("; ", redditSignals)}
              news_score:      {newsScore}/20     news_data:      {string.Join("; ", newsSignals)}
              volume_score:    {volumeScore}/20   volume_data:    {volumeData}
              options_score:   {optionsScore}/20  options_data:   unavailable (coming soon)
              sentiment_score: {sentimentScore}/20 sentiment_data: {sentimentData}

            REQUEST: Generate a full Trading Intelligence Report for this ticker.
            Apply all signal scoring rules, consistency checks, and produce the
            structured output format exactly as specified in your instructions.
            """;
    }
}
