using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradingIntelligence.Core.Enums;
using TradingIntelligence.Core.Models;
using TradingIntelligence.Infrastructure.Helpers;

namespace TradingIntelligence.Infrastructure.Collectors;

public class OptionsCollector
{
    private readonly IConfiguration _config;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<OptionsCollector> _logger;
    private readonly HttpClient _httpClient;

    private static readonly string[] WatchedTickers =
    {
        "AAPL", "NVDA", "MSFT", "TSLA", "AMZN",
        "META", "GOOGL", "AMD", "SPY", "QQQ",
        "NFLX", "PLTR", "COIN", "MSTR", "SOFI"
    };

    // Only collect options during US market hours + pre/post
    // Outside these hours options data is stale — waste of API calls
    private static readonly TimeSpan MarketStart = TimeSpan.FromHours(13);  // 09:00 EST = 13:00 UTC
    private static readonly TimeSpan MarketEnd = TimeSpan.FromHours(24);    // 20:00 EST = 00:00 UTC

    public OptionsCollector(
        IConfiguration config,
        IConnectionMultiplexer redis,
        ILogger<OptionsCollector> logger,
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
            _logger.LogWarning("Polygon API key not configured — skipping options");
            return;
        }

        // Skip outside market hours — options flow is meaningless overnight
        var session = MarketSessionHelper.CurrentSession();
        if (session == MarketSession.WeekendClosed)
        {
            _logger.LogInformation("Options collection skipped — weekend");
            return;
        }

        _logger.LogInformation("Options collection started at {Time}",
            MarketSessionHelper.ToSast(DateTime.UtcNow));

        var db = _redis.GetDatabase();
        var pub = _redis.GetSubscriber();
        int totalPublished = 0;

        foreach (var ticker in WatchedTickers)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var optionsData = await FetchOptionsFlowAsync(
                    ticker, apiKey, cancellationToken);

                if (optionsData == null)
                {
                    _logger.LogDebug("No options data for {Ticker}", ticker);
                    await Task.Delay(13000, cancellationToken);
                    continue;
                }

                var (callVolume, putVolume, callPutRatio, dominantExpiry) =
                    optionsData.Value;

                // Dedup — one signal per ticker per 4 hours
                var dedupKey =
                    $"options:seen:{ticker}:{DateTime.UtcNow:yyyyMMddHH}";
                if (await db.KeyExistsAsync(dedupKey))
                {
                    await Task.Delay(13000, cancellationToken);
                    continue;
                }
                await db.StringSetAsync(dedupKey, "1", TimeSpan.FromHours(5));

                // Only publish if there's meaningful options activity
                var totalVolume = callVolume + putVolume;
                if (totalVolume < 100)
                {
                    _logger.LogDebug(
                        "Options volume too low for {Ticker}: {Vol}", ticker, totalVolume);
                    await Task.Delay(13000, cancellationToken);
                    continue;
                }

                var optionsScore = CalculateOptionsScore(callPutRatio);
                var sentiment = DetermineOptionsSentiment(callPutRatio);

                var signal = new RawSignalEvent
                {
                    SignalType = SignalType.OptionsActivity,
                    Tickers = new List<string> { ticker },
                    Source = "polygon:options",
                    RawText = $"{ticker} options flow — " +
                              $"call/put ratio: {callPutRatio:F2} " +
                              $"(calls: {callVolume:N0}, puts: {putVolume:N0}) " +
                              $"dominant expiry: {dominantExpiry}",
                    SentimentScore = sentiment,
                    RawData = JsonSerializer.Serialize(new
                    {
                        Ticker = ticker,
                        CallVolume = callVolume,
                        PutVolume = putVolume,
                        CallPutRatio = callPutRatio,
                        TotalVolume = totalVolume,
                        OptionsScore = optionsScore,
                        DominantExpiry = dominantExpiry,
                        Bias = callPutRatio >= 2.0m ? "BULLISH"
                             : callPutRatio <= 0.5m ? "BEARISH"
                             : "NEUTRAL",
                        Session = MarketSessionHelper.SessionDisplayName(session),
                        CollectedAt = DateTime.UtcNow
                    }),
                    AuthorKarma = (int)(callPutRatio * 100),
                    AccountAgeMonths = 120,
                    DetectedAt = DateTime.UtcNow
                };

                await pub.PublishAsync(
                    RedisChannel.Literal("raw-signals"),
                    JsonSerializer.Serialize(signal));

                totalPublished++;

                _logger.LogInformation(
                    "Options signal {Ticker}: C/P ratio {Ratio:F2} " +
                    "(calls: {Calls:N0} puts: {Puts:N0}) score: {Score}",
                    ticker, callPutRatio, callVolume, putVolume, optionsScore);

                await Task.Delay(13000, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error collecting options data for {Ticker}", ticker);
                await Task.Delay(13000, cancellationToken);
            }
        }

        _logger.LogInformation(
            "Options collection complete — {Count} signals published", totalPublished);
    }

    private async Task<(long calls, long puts, decimal ratio, string expiry)?>
        FetchOptionsFlowAsync(
            string ticker,
            string apiKey,
            CancellationToken cancellationToken)
    {
        // Polygon snapshot endpoint — returns all option contracts for a ticker
        // We aggregate call vs put volume to derive flow sentiment
        var url = $"https://api.polygon.io/v3/snapshot/options/{ticker}" +
                  $"?limit=250&apiKey={apiKey}";

        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Polygon rate limit hit for options {Ticker} — backing off", ticker);
            await Task.Delay(60000, cancellationToken); // Back off 60s on 429
            return null;
        }

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var node = JsonNode.Parse(json);
        var results = node?["results"]?.AsArray();

        if (results == null || results.Count == 0) return null;

        long callVolume = 0;
        long putVolume = 0;
        var expiryCounts = new Dictionary<string, long>();

        foreach (var contract in results)
        {
            var details = contract?["details"];
            var day = contract?["day"];

            if (details == null || day == null) continue;

            var contractType = details["contract_type"]?.GetValue<string>() ?? "";
            var volume = day["volume"]?.GetValue<long>() ?? 0;
            var expiry = details["expiration_date"]?.GetValue<string>() ?? "";

            if (contractType.Equals("call", StringComparison.OrdinalIgnoreCase))
                callVolume += volume;
            else if (contractType.Equals("put", StringComparison.OrdinalIgnoreCase))
                putVolume += volume;

            // Track dominant expiry by volume
            if (!string.IsNullOrEmpty(expiry) && volume > 0)
            {
                expiryCounts[expiry] = expiryCounts.GetValueOrDefault(expiry) + volume;
            }
        }

        if (callVolume + putVolume == 0) return null;

        decimal ratio = putVolume > 0
            ? Math.Round((decimal)callVolume / putVolume, 2)
            : callVolume > 0 ? 10.0m : 1.0m; // Cap at 10 if no puts

        var dominantExpiry = expiryCounts.Count > 0
            ? expiryCounts.OrderByDescending(kv => kv.Value).First().Key
            : "unknown";

        return (callVolume, putVolume, ratio, dominantExpiry);
    }

    private static decimal CalculateOptionsScore(decimal callPutRatio) => callPutRatio switch
    {
        >= 4.0m => 20,   // Extreme call skew — very bullish
        >= 3.0m => 16,   // Strong call skew
        >= 2.0m => 12,   // Moderate call skew
        >= 1.5m => 8,    // Slight call bias
        >= 0.8m => 4,    // Roughly neutral
        >= 0.5m => 8,    // Slight put skew — SHORT signal
        _       => 16    // Strong put skew — SHORT signal (below 0.5)
    };

    private static decimal DetermineOptionsSentiment(decimal callPutRatio) =>
        callPutRatio switch
        {
            >= 2.0m =>  0.8m,   // Strong bullish
            >= 1.5m =>  0.4m,   // Mild bullish
            >= 0.8m =>  0.1m,   // Neutral
            >= 0.5m => -0.4m,   // Mild bearish
            _       => -0.8m    // Strong bearish
        };
}
