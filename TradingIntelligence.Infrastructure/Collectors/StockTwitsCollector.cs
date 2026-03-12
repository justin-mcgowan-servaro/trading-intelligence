using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradingIntelligence.Core.Enums;
using TradingIntelligence.Core.Models;
using TradingIntelligence.Infrastructure.Helpers;

namespace TradingIntelligence.Infrastructure.Collectors;

public class StockTwitsCollector
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<StockTwitsCollector> _logger;
    private readonly HttpClient _httpClient;

    // Top tickers to monitor on StockTwits
    // These are the most actively discussed on the platform
    private static readonly string[] WatchedTickers =
    {
        "AAPL", "NVDA", "MSFT", "TSLA", "AMZN",
        "META", "GOOGL", "AMD", "SPY", "QQQ",
        "NFLX", "PLTR", "COIN", "MSTR", "SOFI"
    };

    public StockTwitsCollector(
        IConnectionMultiplexer redis,
        ILogger<StockTwitsCollector> logger,
        IHttpClientFactory httpClientFactory)
    {
        _redis = redis;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("StockTwits");
    }

    public async Task CollectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("StockTwits collection started at {Time}",
            MarketSessionHelper.ToSast(DateTime.UtcNow));

        var db = _redis.GetDatabase();
        var pub = _redis.GetSubscriber();
        int totalPublished = 0;

        foreach (var ticker in WatchedTickers)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var messages = await FetchTickerMessagesAsync(ticker, cancellationToken);

                if (!messages.Any())
                {
                    _logger.LogDebug("No messages found for {Ticker}", ticker);
                    continue;
                }

                _logger.LogInformation("StockTwits {Ticker}: {Count} messages fetched",
                    ticker, messages.Count);

                // Calculate velocity — messages in last 2 hours vs last 24 hours
                var last2hrs = messages.Count(m =>
                    m.CreatedAtParsed > DateTime.UtcNow.AddHours(-2));
                var last24hrs = messages.Count(m =>
                    m.CreatedAtParsed > DateTime.UtcNow.AddHours(-24));

                double velocity = last24hrs > 0
                    ? Math.Round((double)last2hrs / last24hrs * 100, 1)
                    : 0;

                // Calculate sentiment from StockTwits built-in labels
                var bullishCount = messages.Count(m =>
                    m.Sentiment?.Basic?.Equals("Bullish",
                        StringComparison.OrdinalIgnoreCase) == true);
                var bearishCount = messages.Count(m =>
                    m.Sentiment?.Basic?.Equals("Bearish",
                        StringComparison.OrdinalIgnoreCase) == true);
                var labelledCount = bullishCount + bearishCount;

                decimal sentimentScore = 0;
                if (labelledCount > 0)
                {
                    // Normalise to -1.0 to +1.0
                    sentimentScore = (decimal)(bullishCount - bearishCount) / labelledCount;
                }
                else
                {
                    // Fall back to text-based sentiment if no labels
                    var allText = string.Join(" ", messages.Select(m => m.Body));
                    sentimentScore = SentimentAnalyser.Score(allText);
                }

                // Dedup check — skip if we already processed this ticker recently
                var dedupKey = $"stocktwits:seen:{ticker}:{DateTime.UtcNow:yyyyMMddHH}";
                if (await db.KeyExistsAsync(dedupKey)) continue;
                await db.StringSetAsync(dedupKey, "1", TimeSpan.FromHours(2));

                var signal = new RawSignalEvent
                {
                    SignalType = SignalType.SocialSentiment,
                    Tickers = new List<string> { ticker },
                    Source = "stocktwits",
                    RawText = $"{ticker}: {last24hrs} messages, {bullishCount} bullish, {bearishCount} bearish",
                    SentimentScore = sentimentScore,
                    RawData = JsonSerializer.Serialize(new
                    {
                        Ticker = ticker,
                        TotalMessages = last24hrs,
                        Last2HoursMessages = last2hrs,
                        VelocityPercent = velocity,
                        BullishCount = bullishCount,
                        BearishCount = bearishCount,
                        SentimentScore = sentimentScore,
                        SentimentLabel = SentimentAnalyser.Label(sentimentScore),
                        CollectedAt = DateTime.UtcNow
                    }),
                    AuthorKarma = last24hrs,       // Use message count as quality proxy
                    AccountAgeMonths = 12,          // StockTwits is a verified platform
                    DetectedAt = DateTime.UtcNow
                };

                var payload = JsonSerializer.Serialize(signal);
                await pub.PublishAsync(
                    RedisChannel.Literal("raw-signals"),
                    payload);

                totalPublished++;

                // Respect rate limit — StockTwits allows ~200 requests/hour unauthenticated
                await Task.Delay(500, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting StockTwits data for {Ticker}", ticker);
            }
        }

        _logger.LogInformation(
            "StockTwits collection complete — {Count} signals published to Redis",
            totalPublished);
    }

    private async Task<List<StockTwitsMessage>> FetchTickerMessagesAsync(
        string ticker,
        CancellationToken cancellationToken)
    {
        // StockTwits public stream endpoint — no auth required
        var url = $"https://api.stocktwits.com/api/2/streams/symbol/{ticker}.json?limit=30";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "TradingIntelligence/1.0");

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "StockTwits returned {Status} for {Ticker}",
                response.StatusCode, ticker);
            return new List<StockTwitsMessage>();
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<StockTwitsResponse>(json);

        return result?.Messages?
            .Where(m => m.CreatedAtParsed > DateTime.UtcNow.AddHours(-24))
            .ToList() ?? new List<StockTwitsMessage>();
    }
}
