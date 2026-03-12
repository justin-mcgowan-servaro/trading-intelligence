using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradingIntelligence.Infrastructure.Helpers;

namespace TradingIntelligence.Infrastructure.Collectors;

public class FearGreedCollector
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<FearGreedCollector> _logger;
    private readonly HttpClient _httpClient;

    private const string CacheKey = "feargreed:latest";

    public FearGreedCollector(
        IConnectionMultiplexer redis,
        ILogger<FearGreedCollector> logger,
        IHttpClientFactory httpClientFactory)
    {
        _redis = redis;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("FearGreed");
    }

    public async Task CollectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fear & Greed collection started");

            //var url = "https://production.dataviz.cnn.io/index/fearandgreed/graphdata";
            var url = "https://api.alternative.me/fng/?limit=1&format=json";


            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            request.Headers.Add("Referer", "https://www.cnn.com/");

            var response = await _httpClient.SendAsync(
                request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Fear & Greed endpoint returned {Status}",
                    response.StatusCode);
                return;
            }

            var json = await response.Content
                .ReadAsStringAsync(cancellationToken);
            var node = JsonNode.Parse(json);

            //var score = node?["fear_and_greed"]?["score"]?.GetValue<double>();
            //var rating = node?["fear_and_greed"]?["rating"]
            //    ?.GetValue<string>() ?? "unknown";

            //if (score == null) return;

            var score = node?["data"]?[0]?["value"]?.GetValue<string>();
            var rating = node?["data"]?[0]?["value_classification"]?.GetValue<string>() ?? "unknown";

            if (score == null || !double.TryParse(score, out var scoreValue)) return;

            var result = JsonSerializer.Serialize(new
            {
                Score = Math.Round(scoreValue, 1),
                Rating = rating,
                CollectedAt = DateTime.UtcNow,
                CollectedAtSast = MarketSessionHelper.ToSast(DateTime.UtcNow),
                // Derive market context from score
                Context = scoreValue switch
                {
                    >= 75 => "Extreme Greed — market may be overextended",
                    >= 55 => "Greed — bullish momentum present",
                    >= 45 => "Neutral — no strong directional bias",
                    >= 25 => "Fear — potential buying opportunity",
                    _ => "Extreme Fear — high volatility expected"
                }
            });

            // Cache in Redis for 1 hour — all AI calls read from here
            var db = _redis.GetDatabase();
            await db.StringSetAsync(CacheKey, result, TimeSpan.FromHours(1));

            _logger.LogInformation(
                "Fear & Greed updated — Score: {Score} ({Rating})",
                scoreValue, rating);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting Fear & Greed data");
        }
    }

    // Static helper for other services to read the cached value
    public static async Task<string> GetCachedAsync(IDatabase db)
    {
        var cached = await db.StringGetAsync(CacheKey);
        return cached.HasValue ? cached.ToString() : "unavailable";
    }
}