using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradingIntelligence.Core.Enums;
using TradingIntelligence.Core.Models;
using TradingIntelligence.Infrastructure.Helpers;

namespace TradingIntelligence.Infrastructure.Collectors;

public class NewsApiCollector
{
    private readonly IConfiguration _config;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<NewsApiCollector> _logger;
    private readonly HttpClient _httpClient;

    // High credibility financial sources on NewsAPI
    private static readonly Dictionary<string, double> SourceCredibility =
        new(StringComparer.OrdinalIgnoreCase)
    {
        { "bloomberg",          1.0 },
        { "reuters",            1.0 },
        { "the wall street journal", 1.0 },
        { "financial times",    1.0 },
        { "cnbc",               0.9 },
        { "marketwatch",        0.9 },
        { "forbes",             0.8 },
        { "business insider",   0.7 },
        { "yahoo finance",      0.8 },
        { "seeking alpha",      0.7 },
        { "benzinga",           0.8 },
        { "investor's business daily", 0.9 },
    };

    private static readonly Dictionary<string, string> CompanyToTicker =
    new(StringComparer.OrdinalIgnoreCase)
    {
        { "nvidia",    "NVDA" },
        { "tesla",     "TSLA" },
        { "apple",     "AAPL" },
        { "microsoft", "MSFT" },
        { "meta",      "META" },
        { "alphabet",  "GOOGL" },
        { "google",    "GOOGL" },
        { "amazon",    "AMZN" },
        { "netflix",   "NFLX" },
        { "palantir",  "PLTR" },
        { "amd",       "AMD"  },
        { "intel",     "INTC" },
        { "salesforce","CRM"  },
        { "coinbase",  "COIN" },
        { "shopify",   "SHOP" },
    };
    
    private static readonly Dictionary<string, int> CatalystKeywords =
        new(StringComparer.OrdinalIgnoreCase)
    {
        { "earnings beat",      18 }, { "earnings miss",     16 },
        { "revenue beat",       16 }, { "revenue miss",      14 },
        { "raised guidance",    17 }, { "lowered guidance",  15 },
        { "acquisition",        14 }, { "merger",            13 },
        { "fda approval",       18 }, { "fda rejection",     17 },
        { "analyst upgrade",    14 }, { "analyst downgrade", 14 },
        { "price target raised",15 }, { "price target cut",  14 },
        { "share buyback",      13 }, { "dividend increase", 13 },
        { "ceo resign",         16 }, { "bankruptcy",        18 },
        { "profit warning",     17 }, { "record revenue",    16 },
        { "beat estimates",     16 }, { "miss estimates",    15 },
        { "raised outlook",     16 }, { "cut outlook",       15 },
    };

    public NewsApiCollector(
        IConfiguration config,
        IConnectionMultiplexer redis,
        ILogger<NewsApiCollector> logger,
        IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _redis = redis;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("NewsApi");
    }

    public async Task CollectAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = _config["GNews:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("GNews key not configured — skipping");
            return;
        }

        _logger.LogInformation("NewsAPI collection started at {Time}",
            MarketSessionHelper.ToSast(DateTime.UtcNow));

        var db = _redis.GetDatabase();
        var pub = _redis.GetSubscriber();
        int totalPublished = 0;

        // Search for financial market news
        var queries = new[]
        {
            "Nvidia NVDA earnings revenue",
            "Tesla TSLA earnings delivery",
            "Apple AAPL earnings iPhone",
            "Meta earnings revenue advertising",
            "Microsoft MSFT earnings cloud",
            "stock earnings beat miss analyst upgrade downgrade",
            "merger acquisition deal",
            "FDA approval rejection biotech",
        };
        foreach (var query in queries)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var articles = await FetchArticlesAsync(
                    query, apiKey, cancellationToken);

                _logger.LogInformation(
                    "NewsAPI query '{Query}': {Count} articles",
                    query, articles.Count);

                foreach (var article in articles)
                {
                    if (article.PublishedAtParsed < DateTime.UtcNow.AddHours(-72))
                        continue;
                    if (article.PublishedAtParsed < DateTime.UtcNow.AddHours(-72))
                    {
                        _logger.LogInformation("Skipping old article ({Age:F0}hrs): {Title}",
                            (DateTime.UtcNow - article.PublishedAtParsed).TotalHours,
                            article.Title);
                        continue;
                    }
                    var fullText = $"{article.Title} {article.Description}";
                    var enrichedText = EnrichWithTickers(fullText);
                    var tickers = TickerExtractor.Extract(enrichedText);
                    if (!tickers.Any())
                    {
                        _logger.LogDebug("No tickers found in: {Title}", article.Title);
                        continue;
                    }
                    _logger.LogInformation(
                    "Tickers {Tickers} found in: {Title} (enriched text had {Len} chars)",
                    string.Join(",", tickers), article.Title, enrichedText.Length);

                    // Dedup by URL hash
                    var dedupKey = $"newsapi:seen:{article.Url.GetHashCode()}";
                    if (await db.KeyExistsAsync(dedupKey)) continue;
                    await db.StringSetAsync(dedupKey, "1", TimeSpan.FromHours(25));

                    var credibility = GetSourceCredibility(
                        article.Source?.Name ?? string.Empty);
                    var catalystScore = CalculateCatalystScore(
                        fullText, credibility);
                    var sentiment = SentimentAnalyser.Score(fullText);

                    var signal = new RawSignalEvent
                    {
                        SignalType = SignalType.NewsCatalyst,
                        Tickers = tickers,
                        Source = $"newsapi:{article.Source?.Name ?? "unknown"}",
                        RawText = article.Title,
                        SentimentScore = sentiment,
                        RawData = JsonSerializer.Serialize(new
                        {
                            article.Title,
                            article.Description,
                            article.Url,
                            Source = article.Source?.Name,
                            article.PublishedAtParsed,
                            CatalystScore = catalystScore,
                            CredibilityWeight = credibility,
                            SentimentLabel = SentimentAnalyser.Label(sentiment)
                        }),
                        AuthorKarma = (int)(catalystScore * 10),
                        AccountAgeMonths = 120,
                        DetectedAt = DateTime.UtcNow
                    };

                    var payload = JsonSerializer.Serialize(signal);
                    await pub.PublishAsync(
                        RedisChannel.Literal("raw-signals"), payload);

                    totalPublished++;
                }

                await Task.Delay(1000, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error fetching NewsAPI query '{Query}'", query);
            }
        }

        _logger.LogInformation(
            "NewsAPI collection complete — {Count} signals published",
            totalPublished);
    }

    private async Task<List<NewsApiArticle>> FetchArticlesAsync(
    string query,
    string apiKey,
    CancellationToken cancellationToken)
    {
        var url = $"https://gnews.io/api/v4/search" +
                  $"?q={Uri.EscapeDataString(query)}" +
                  $"&lang=en&country=us&max=10" +
                  $"&apikey={apiKey}";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode) return new List<NewsApiArticle>();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<NewsApiResponse>(json);

        return result?.Articles ?? new List<NewsApiArticle>();
    }
    private static double GetSourceCredibility(string sourceName)
    {
        foreach (var (key, value) in SourceCredibility)
            if (sourceName.Contains(key, StringComparison.OrdinalIgnoreCase))
                return value;
        return 0.6; // Default credibility for unknown sources
    }

    private static decimal CalculateCatalystScore(
        string text, double credibility)
    {
        int rawScore = 5;
        foreach (var (keyword, score) in CatalystKeywords)
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                rawScore = Math.Max(rawScore, score);

        return (decimal)Math.Min(20,
            Math.Max(0, Math.Round(rawScore * credibility, 1)));
    }
    private static string EnrichWithTickers(string text)
    {
        var sb = new System.Text.StringBuilder(text);
        foreach (var (company, ticker) in CompanyToTicker)
        {
            // Check if company name appears in text (case-insensitive)
            if (text.Contains(company, StringComparison.OrdinalIgnoreCase))
            {
                // Append the ticker symbol in $ format (high confidence)
                sb.Append($" ${ticker}");
            }
        }
        return sb.ToString();
    }
}
