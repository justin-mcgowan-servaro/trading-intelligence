using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradingIntelligence.Core.Enums;
using TradingIntelligence.Core.Models;
using TradingIntelligence.Infrastructure.Helpers;

namespace TradingIntelligence.Infrastructure.Collectors;

public class GoogleTrendsCollector
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<GoogleTrendsCollector> _logger;
    private readonly HttpClient _httpClient;

    private static readonly string[] WatchedTickers =
    {
        "AAPL", "NVDA", "MSFT", "TSLA", "AMZN",
        "META", "GOOGL", "AMD", "PLTR", "COIN"
    };

    public GoogleTrendsCollector(
        IConnectionMultiplexer redis,
        ILogger<GoogleTrendsCollector> logger,
        IHttpClientFactory httpClientFactory)
    {
        _redis = redis;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("GoogleTrends");
    }

    public async Task CollectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Google Trends collection started at {Time}",
            MarketSessionHelper.ToSast(DateTime.UtcNow));

        var db = _redis.GetDatabase();
        var pub = _redis.GetSubscriber();
        int totalPublished = 0;

        foreach (var ticker in WatchedTickers)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var trendScore = await FetchTrendScoreAsync(
                    ticker, cancellationToken);

                if (trendScore < 40) continue; // Only notable trends

                _logger.LogInformation(
                    "Google Trends spike {Ticker}: score {Score}",
                    ticker, trendScore);

                var dedupKey =
                    $"gtrends:seen:{ticker}:{DateTime.UtcNow:yyyyMMddHH}";
                if (await db.KeyExistsAsync(dedupKey)) continue;
                await db.StringSetAsync(
                    dedupKey, "1", TimeSpan.FromHours(2));

                // Map trend score to sentiment
                decimal sentiment = trendScore switch
                {
                    >= 80 => 0.7m,
                    >= 60 => 0.4m,
                    >= 40 => 0.2m,
                    _ => 0m
                };

                var signal = new RawSignalEvent
                {
                    SignalType = SignalType.RedditMomentum,
                    Tickers = new List<string> { ticker },
                    Source = "google_trends",
                    RawText = $"{ticker} Google search interest: {trendScore}/100",
                    SentimentScore = sentiment,
                    RawData = JsonSerializer.Serialize(new
                    {
                        Ticker = ticker,
                        TrendScore = trendScore,
                        CollectedAt = DateTime.UtcNow,
                        Note = "7-day search interest index 0-100"
                    }),
                    AuthorKarma = trendScore,
                    AccountAgeMonths = 12,
                    DetectedAt = DateTime.UtcNow
                };

                await pub.PublishAsync(
                    RedisChannel.Literal("raw-signals"),
                    JsonSerializer.Serialize(signal));

                totalPublished++;

                await Task.Delay(2000, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error fetching Google Trends for {Ticker}", ticker);
            }
        }

        _logger.LogInformation(
            "Google Trends collection complete — {Count} signals published",
            totalPublished);
    }

    private async Task<int> FetchTrendScoreAsync(
        string ticker,
        CancellationToken cancellationToken)
    {
        // Google Trends RSS feed — no API key required
        var url = $"https://trends.google.com/trends/api/dailytrends" +
                  $"?hl=en-US&tz=-120&geo=US&ns=15";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

        var response = await _httpClient.SendAsync(
            request, cancellationToken);

        if (!response.IsSuccessStatusCode) return 0;

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        // Google prepends ")]}'\n" to prevent JSON hijacking
        var json = raw.StartsWith(")]}'")
            ? raw.Substring(raw.IndexOf('\n') + 1)
            : raw;

        // Check if ticker appears in trending topics
        var tickerCount = CountOccurrences(json, ticker);

        // Map occurrence count to a 0-100 score
        return tickerCount switch
        {
            >= 5 => 90,
            >= 3 => 70,
            >= 2 => 55,
            >= 1 => 40,
            _ => 0
        };
    }

    private static int CountOccurrences(string text, string term)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(term,
            index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += term.Length;
        }
        return count;
    }
}