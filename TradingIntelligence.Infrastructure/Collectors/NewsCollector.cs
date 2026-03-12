using System;
using System.Collections.Generic;
using System.Text;
using System.ServiceModel.Syndication;
using System.Text.Json;
using System.Xml;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradingIntelligence.Core.Enums;
using TradingIntelligence.Core.Models;
using TradingIntelligence.Infrastructure.Helpers;

namespace TradingIntelligence.Infrastructure.Collectors;

public class NewsCollector
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<NewsCollector> _logger;
    private readonly HttpClient _httpClient;

    // Free RSS feeds — no API key required
    // Credibility weight: 1.0 = top tier, 0.7 = mid tier, 0.5 = lower tier
    private static readonly (string Url, string Name, double Credibility)[] Feeds =
    {
        ("https://feeds.finance.yahoo.com/rss/2.0/headline?s=&region=US&lang=en-US",
            "Yahoo Finance", 0.8),
        ("https://feeds.marketwatch.com/marketwatch/topstories/",
            "MarketWatch", 0.9),
        ("https://feeds.reuters.com/reuters/businessNews",
            "Reuters Business", 1.0),
        ("https://feeds.bloomberg.com/markets/news.rss",
            "Bloomberg Markets", 1.0),
        ("https://www.investing.com/rss/news.rss",
            "Investing.com", 0.7),
        ("https://feeds.feedburner.com/businessinsider",
            "Business Insider", 0.7),
        ("https://finance.yahoo.com/news/rssindex",
            "Yahoo Finance News", 0.8),
    };

    // Keywords that indicate a high-impact catalyst
    private static readonly Dictionary<string, int> CatalystKeywords = new(
        StringComparer.OrdinalIgnoreCase)
    {
        { "earnings beat", 18 },    { "earnings miss", 16 },
        { "revenue beat", 16 },     { "revenue miss", 14 },
        { "raised guidance", 17 },  { "lowered guidance", 15 },
        { "acquisition", 14 },      { "merger", 13 },
        { "partnership", 12 },      { "fda approval", 18 },
        { "fda rejection", 17 },    { "analyst upgrade", 14 },
        { "analyst downgrade", 14 },{ "price target raised", 15 },
        { "price target cut", 14 }, { "share buyback", 13 },
        { "dividend increase", 13 },{ "ceo resign", 16 },
        { "ceo fired", 16 },        { "layoffs", 12 },
        { "bankruptcy", 18 },       { "investigation", 15 },
        { "lawsuit", 13 },          { "record revenue", 16 },
        { "profit warning", 17 },   { "guidance raised", 17 },
    };

    public NewsCollector(
        IConnectionMultiplexer redis,
        ILogger<NewsCollector> logger,
        IHttpClientFactory httpClientFactory)
    {
        _redis = redis;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("News");
    }

    public async Task CollectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("News collection started at {Time}",
            MarketSessionHelper.ToSast(DateTime.UtcNow));

        var db = _redis.GetDatabase();
        var pub = _redis.GetSubscriber();
        int totalPublished = 0;

        foreach (var (url, name, credibility) in Feeds)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var items = await FetchFeedAsync(url, name, credibility, cancellationToken);
                _logger.LogInformation("{Source}: fetched {Count} items", name, items.Count);

                foreach (var item in items)
                {
                    // Only process news from last 24 hours
                    if (item.PublishedAt < DateTime.UtcNow.AddHours(-24)) continue;

                    // Extract tickers from headline
                    var fullText = $"{item.Title} {item.Description}";
                    var tickers = TickerExtractor.Extract(fullText);
                    if (!tickers.Any()) continue;

                    // Dedup by URL hash
                    var dedupKey = $"news:seen:{item.Link.GetHashCode()}";
                    if (await db.KeyExistsAsync(dedupKey)) continue;
                    await db.StringSetAsync(dedupKey, "1", TimeSpan.FromHours(25));

                    // Calculate news catalyst score (0-20)
                    var catalystScore = CalculateCatalystScore(item, credibility);

                    // Calculate sentiment
                    var sentiment = SentimentAnalyser.Score(fullText);

                    var signal = new RawSignalEvent
                    {
                        SignalType = SignalType.NewsCatalyst,
                        Tickers = tickers,
                        Source = $"news:{name.ToLower().Replace(" ", "_")}",
                        RawText = item.Title,
                        SentimentScore = sentiment,
                        RawData = JsonSerializer.Serialize(new
                        {
                            item.Title,
                            item.Description,
                            item.Link,
                            item.Source,
                            item.PublishedAt,
                            CatalystScore = catalystScore,
                            CredibilityWeight = credibility,
                            SentimentLabel = SentimentAnalyser.Label(sentiment)
                        }),
                        AuthorKarma = (int)(catalystScore * 10),
                        AccountAgeMonths = 120, // News sources are established
                        DetectedAt = DateTime.UtcNow
                    };

                    var payload = JsonSerializer.Serialize(signal);
                    await pub.PublishAsync(
                        RedisChannel.Literal("raw-signals"),
                        payload);

                    totalPublished++;
                }

                await Task.Delay(500, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting from {Source}", name);
            }
        }

        _logger.LogInformation(
            "News collection complete — {Count} signals published to Redis",
            totalPublished);
    }

    private async Task<List<NewsItem>> FetchFeedAsync(
        string url,
        string name,
        double credibility,
        CancellationToken cancellationToken)
    {
        var items = new List<NewsItem>();

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return items;

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            using var xmlReader = XmlReader.Create(stream,
                new XmlReaderSettings { Async = true });

            var feed = SyndicationFeed.Load(xmlReader);

            foreach (var item in feed.Items)
            {
                items.Add(new NewsItem
                {
                    Title = item.Title?.Text ?? string.Empty,
                    Description = item.Summary?.Text ?? string.Empty,
                    Link = item.Links.FirstOrDefault()?.Uri?.ToString() ?? string.Empty,
                    Source = name,
                    CredibilityWeight = credibility,
                    PublishedAt = item.PublishDate.UtcDateTime == DateTime.MinValue
                        ? DateTime.UtcNow
                        : item.PublishDate.UtcDateTime
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse RSS feed from {Source}", name);
        }

        return items;
    }

    private static decimal CalculateCatalystScore(NewsItem item, double credibility)
    {
        var text = $"{item.Title} {item.Description}".ToLowerInvariant();
        int rawScore = 5; // Base score for any financial news

        // Check for catalyst keywords
        foreach (var (keyword, score) in CatalystKeywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                rawScore = Math.Max(rawScore, score);
            }
        }

        // Apply credibility multiplier
        var weighted = rawScore * credibility;

        // Clamp to 0-20 range
        return (decimal)Math.Min(20, Math.Max(0, Math.Round(weighted, 1)));
    }
}
