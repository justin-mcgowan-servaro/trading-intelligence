using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradingIntelligence.Core.Enums;
using TradingIntelligence.Core.Models;
using TradingIntelligence.Infrastructure.Helpers;

namespace TradingIntelligence.Infrastructure.Collectors;

public class VolumeCollector
{
    private readonly IConfiguration _config;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<VolumeCollector> _logger;
    private readonly HttpClient _httpClient;

    // Tickers to monitor for volume spikes
    private static readonly string[] WatchedTickers =
    {
        "AAPL", "NVDA", "MSFT", "TSLA", "AMZN",
        "META", "GOOGL", "AMD", "SPY", "QQQ"
    };

    public VolumeCollector(
        IConfiguration config,
        IConnectionMultiplexer redis,
        ILogger<VolumeCollector> logger,
        IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _redis = redis;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("Volume");
    }

    public async Task CollectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Volume collection started at {Time}",
            MarketSessionHelper.ToSast(DateTime.UtcNow));

        var apiKey = _config["AlphaVantage:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("AlphaVantage API key not configured — skipping volume collection");
            return;
        }

        var db = _redis.GetDatabase();
        var pub = _redis.GetSubscriber();
        int totalPublished = 0;

        foreach (var ticker in WatchedTickers)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var volumeData = await FetchVolumeDataAsync(ticker, apiKey, cancellationToken);
                if (volumeData == null) continue;

                var (currentVolume, avgVolume, volumeRatio, currentPrice) = volumeData.Value;

                // Only publish if volume is notable (>1.3x average)
                if (volumeRatio < (decimal)1.3) continue;

                _logger.LogInformation(
                    "Volume spike detected — {Ticker}: {Ratio:F2}x average volume",
                    ticker, volumeRatio);

                // Dedup — one signal per ticker per hour
                var dedupKey = $"volume:seen:{ticker}:{DateTime.UtcNow:yyyyMMddHH}";
                if (await db.KeyExistsAsync(dedupKey)) continue;
                await db.StringSetAsync(dedupKey, "1", TimeSpan.FromHours(2));

                // Calculate volume score (0-20)
                var volumeScore = CalculateVolumeScore(volumeRatio);

                var signal = new RawSignalEvent
                {
                    SignalType = SignalType.VolumeSpike,
                    Tickers = new List<string> { ticker },
                    Source = "alphavantage",
                    RawText = $"{ticker} volume {volumeRatio:F2}x average — " +
                              $"current: {currentVolume:N0}, avg: {avgVolume:N0}",
                    SentimentScore = volumeRatio > 2.0m ? 0.5m : 0.2m,
                    RawData = JsonSerializer.Serialize(new
                    {
                        Ticker = ticker,
                        CurrentVolume = currentVolume,
                        AverageVolume20Day = avgVolume,
                        VolumeRatio = volumeRatio,
                        VolumeScore = volumeScore,
                        CurrentPrice = currentPrice,
                        CollectedAt = DateTime.UtcNow,
                        Session = MarketSessionHelper.SessionDisplayName(
                            MarketSessionHelper.CurrentSession())
                    }),
                    AuthorKarma = (int)(volumeRatio * 100),
                    AccountAgeMonths = 120,
                    DetectedAt = DateTime.UtcNow
                };

                var payload = JsonSerializer.Serialize(signal);
                await pub.PublishAsync(
                    RedisChannel.Literal("raw-signals"),
                    payload);

                totalPublished++;

                // Alpha Vantage free tier: 25 requests/day
                // Add delay to stay within limits
                await Task.Delay(15000, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting volume data for {Ticker}", ticker);
            }
        }

        _logger.LogInformation(
            "Volume collection complete — {Count} volume spikes published",
            totalPublished);
    }

    private async Task<(decimal current, decimal avg, decimal ratio, decimal price)?>
        FetchVolumeDataAsync(
            string ticker,
            string apiKey,
            CancellationToken cancellationToken)
    {
        // TIME_SERIES_DAILY gives us 100 days of OHLCV data
        var url = $"https://www.alphavantage.co/query" +
                  $"?function=TIME_SERIES_DAILY" +
                  $"&symbol={ticker}" +
                  $"&outputsize=compact" +
                  $"&apikey={apiKey}";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var node = JsonNode.Parse(json);

        // Check for API limit message
        if (node?["Note"] != null || node?["Information"] != null)
        {
            _logger.LogWarning(
                "Alpha Vantage API limit reached for {Ticker}", ticker);
            return null;
        }

        var timeSeries = node?["Time Series (Daily)"];
        if (timeSeries == null) return null;

        var dates = timeSeries.AsObject()
            .Select(kv => kv.Key)
            .OrderByDescending(d => d)
            .ToList();

        if (dates.Count < 21) return null;

        // Most recent day
        var latestDate = dates[0];
        var latestData = timeSeries[latestDate];
        if (latestData == null) return null;

        decimal currentVolume = decimal.Parse(
            latestData["5. volume"]?.ToString() ?? "0");
        decimal currentPrice = decimal.Parse(
            latestData["4. close"]?.ToString() ?? "0");

        // 20-day average volume (days 1-20, skipping today)
        decimal totalVolume = 0;
        int count = 0;
        for (int i = 1; i <= 20 && i < dates.Count; i++)
        {
            var dayData = timeSeries[dates[i]];
            if (dayData == null) continue;

            if (decimal.TryParse(dayData["5. volume"]?.ToString(), out var vol))
            {
                totalVolume += vol;
                count++;
            }
        }

        if (count == 0) return null;

        decimal avgVolume = totalVolume / count;
        decimal volumeRatio = avgVolume > 0
            ? Math.Round(currentVolume / avgVolume, 2)
            : 0;

        return (currentVolume, avgVolume, volumeRatio, currentPrice);
    }

    private static decimal CalculateVolumeScore(decimal ratio) => ratio switch
    {
        >= 3.0m => 20,
        >= 2.5m => 16,
        >= 2.0m => 12,
        >= 1.5m => 8,
        >= 1.3m => 5,
        _ => 0
    };
}