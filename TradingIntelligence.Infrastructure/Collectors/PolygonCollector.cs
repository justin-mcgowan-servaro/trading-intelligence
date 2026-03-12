using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradingIntelligence.Core.Enums;
using TradingIntelligence.Core.Models;
using TradingIntelligence.Infrastructure.Helpers;

namespace TradingIntelligence.Infrastructure.Collectors;

public class PolygonCollector
{
    private readonly IConfiguration _config;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PolygonCollector> _logger;
    private readonly HttpClient _httpClient;

    private static readonly string[] WatchedTickers =
    {
        "AAPL", "NVDA", "MSFT", "TSLA", "AMZN",
        "META", "GOOGL", "AMD", "SPY", "QQQ",
        "NFLX", "PLTR", "COIN", "MSTR", "SOFI"
    };

    public PolygonCollector(
        IConfiguration config,
        IConnectionMultiplexer redis,
        ILogger<PolygonCollector> logger,
        IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _redis = redis;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("Polygon");
    }

    public async Task CollectAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = _config["Polygon:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Polygon API key not configured — skipping");
            return;
        }

        _logger.LogInformation("Polygon collection started at {Time}",
            MarketSessionHelper.ToSast(DateTime.UtcNow));

        var db = _redis.GetDatabase();
        var pub = _redis.GetSubscriber();
        int totalPublished = 0;

        foreach (var ticker in WatchedTickers)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                // Get previous close data for volume comparison
                var volumeData = await FetchVolumeDataAsync(
                    ticker, apiKey, cancellationToken);

                if (volumeData == null) continue;

                var (currentVolume, avgVolume, volumeRatio, currentPrice) =
                    volumeData.Value;

                // Dedup — one signal per ticker per hour
                var dedupKey =
                    $"polygon:seen:{ticker}:{DateTime.UtcNow:yyyyMMddHH}";
                if (await db.KeyExistsAsync(dedupKey)) continue;
                await db.StringSetAsync(dedupKey, "1", TimeSpan.FromHours(2));

                // Only publish if volume is notable
                if (volumeRatio >= 1.2m)
                {
                    var volumeScore = CalculateVolumeScore(volumeRatio);

                    var volumeSignal = new RawSignalEvent
                    {
                        SignalType = SignalType.VolumeSpike,
                        Tickers = new List<string> { ticker },
                        Source = "polygon",
                        RawText = $"{ticker} volume {volumeRatio:F2}x average — " +
                                  $"current: {currentVolume:N0}, " +
                                  $"avg: {avgVolume:N0}",
                        SentimentScore = volumeRatio > 2.0m ? 0.5m : 0.2m,
                        RawData = JsonSerializer.Serialize(new
                        {
                            Ticker = ticker,
                            CurrentVolume = currentVolume,
                            AverageVolume = avgVolume,
                            VolumeRatio = volumeRatio,
                            VolumeScore = volumeScore,
                            CurrentPrice = currentPrice,
                            Session = MarketSessionHelper.SessionDisplayName(
                                MarketSessionHelper.CurrentSession()),
                            CollectedAt = DateTime.UtcNow
                        }),
                        AuthorKarma = (int)(volumeRatio * 100),
                        AccountAgeMonths = 120,
                        DetectedAt = DateTime.UtcNow
                    };

                    await pub.PublishAsync(
                        RedisChannel.Literal("raw-signals"),
                        JsonSerializer.Serialize(volumeSignal));

                    totalPublished++;

                    _logger.LogInformation(
                        "Polygon volume spike {Ticker}: {Ratio:F2}x average",
                        ticker, volumeRatio);
                }

                // Small delay to respect rate limits
                await Task.Delay(2000, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error collecting Polygon data for {Ticker}", ticker);
            }
        }

        _logger.LogInformation(
            "Polygon collection complete — {Count} signals published",
            totalPublished);
    }

    private async Task<(decimal current, decimal avg, decimal ratio, decimal price)?>
        FetchVolumeDataAsync(
            string ticker,
            string apiKey,
            CancellationToken cancellationToken)
    {
        // Get previous 30 days of daily aggregates
        var to = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var from = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd");

        var url = $"https://api.polygon.io/v2/aggs/ticker/{ticker}/range/1/day" +
                  $"/{from}/{to}" +
                  $"?adjusted=true&sort=desc&limit=30&apiKey={apiKey}";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var node = JsonNode.Parse(json);

        var results = node?["results"]?.AsArray();
        if (results == null || results.Count < 2) return null;

        // Most recent day
        var latest = results[0];
        decimal currentVolume = latest?["v"]?.GetValue<decimal>() ?? 0;
        decimal currentPrice = latest?["c"]?.GetValue<decimal>() ?? 0;

        // 20-day average volume
        decimal totalVolume = 0;
        int count = 0;
        for (int i = 1; i < Math.Min(21, results.Count); i++)
        {
            var vol = results[i]?["v"]?.GetValue<decimal>() ?? 0;
            if (vol > 0) { totalVolume += vol; count++; }
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
        >= 1.2m => 5,
        _ => 0
    };
}