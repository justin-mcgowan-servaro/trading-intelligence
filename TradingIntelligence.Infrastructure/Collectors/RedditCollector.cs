using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradingIntelligence.Core.Enums;
using TradingIntelligence.Core.Models;
using TradingIntelligence.Infrastructure.Helpers;

namespace TradingIntelligence.Infrastructure.Collectors;

public class RedditCollector
{
    private readonly IConfiguration _config;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedditCollector> _logger;
    private readonly HttpClient _httpClient;

    private string _accessToken = string.Empty;
    private DateTime _tokenExpiry = DateTime.MinValue;

    private static readonly string[] Subreddits =
    {
        "stocks", "wallstreetbets", "investing", "options", "cryptocurrency"
    };

    public RedditCollector(
        IConfiguration config,
        IConnectionMultiplexer redis,
        ILogger<RedditCollector> logger,
        IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _redis = redis;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("Reddit");
    }

    public async Task CollectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reddit collection started at {Time} SAST",
            MarketSessionHelper.ToSast(DateTime.UtcNow));

        try
        {
            await EnsureTokenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to authenticate with Reddit API");
            return;
        }

        var db = _redis.GetDatabase();
        var pub = _redis.GetSubscriber();
        int totalPublished = 0;

        foreach (var subreddit in Subreddits)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var posts = await FetchPostsAsync(subreddit, cancellationToken);
                _logger.LogInformation("r/{Sub}: fetched {Count} posts", subreddit, posts.Count);

                foreach (var post in posts)
                {
                    // Only process posts from last 24 hours
                    if (post.CreatedDateTime < DateTime.UtcNow.AddHours(-24)) continue;

                    // Deduplication — skip if seen in last 25 hours
                    var dedupKey = $"reddit:seen:{post.Id}";
                    if (await db.KeyExistsAsync(dedupKey)) continue;
                    await db.StringSetAsync(dedupKey, "1", TimeSpan.FromHours(25));

                    // Extract tickers from title + body
                    var fullText = $"{post.Title} {post.Selftext}";
                    var tickers = TickerExtractor.Extract(fullText);

                    if (!tickers.Any()) continue;

                    // Calculate sentiment
                    var sentiment = SentimentAnalyser.Score(fullText);

                    // Build signal event
                    var signal = new RawSignalEvent
                    {
                        SignalType = SignalType.RedditMomentum,
                        Tickers = tickers,
                        Source = $"reddit:r/{subreddit}",
                        RawText = post.Title,
                        SentimentScore = sentiment,
                        RawData = JsonSerializer.Serialize(new
                        {
                            post.Id,
                            post.Title,
                            post.Score,
                            post.NumComments,
                            post.UpvoteRatio,
                            post.Author,
                            post.Flair,
                            Subreddit = subreddit,
                            CreatedUtc = post.CreatedDateTime
                        }),
                        // Reddit API doesn't return account age easily
                        // so we use post score as a quality proxy
                        // Posts with score > 5 pass basic quality check
                        AuthorKarma = post.Score,
                        AccountAgeMonths = post.Score > 0 ? 6 : 0,
                        DetectedAt = DateTime.UtcNow
                    };

                    // Publish to Redis channel
                    var payload = JsonSerializer.Serialize(signal);
                    await pub.PublishAsync(
                        RedisChannel.Literal("raw-signals"),
                        payload);

                    totalPublished++;
                }

                // Small delay between subreddits to respect rate limits
                await Task.Delay(1000, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting from r/{Sub}", subreddit);
            }
        }

        _logger.LogInformation(
            "Reddit collection complete — {Count} signals published to Redis",
            totalPublished);
    }

    //private async Task<List<RedditPost>> FetchPostsAsync(
    //    string subreddit,
    //    CancellationToken cancellationToken)
    //{
    //    var posts = new List<RedditPost>();

    //    // Fetch both hot and new to maximise coverage
    //    foreach (var sort in new[] { "hot", "new" })
    //    {
    //        var request = new HttpRequestMessage(
    //            HttpMethod.Get,
    //            $"https://oauth.reddit.com/r/{subreddit}/{sort}?limit=50");

    //        request.Headers.Authorization =
    //            new AuthenticationHeaderValue("Bearer", _accessToken);
    //        request.Headers.Add("User-Agent",
    //            _config["Reddit:UserAgent"] ?? "TradingIntelligence/1.0");

    //        var response = await _httpClient.SendAsync(request, cancellationToken);

    //        if (!response.IsSuccessStatusCode)
    //        {
    //            _logger.LogWarning(
    //                "Reddit API returned {Status} for r/{Sub}/{Sort}",
    //                response.StatusCode, subreddit, sort);
    //            continue;
    //        }

    //        var json = await response.Content.ReadAsStringAsync(cancellationToken);
    //        var listing = JsonSerializer.Deserialize<RedditListing>(json);

    //        var fetchedPosts = listing?.Data?.Children?
    //            .Where(c => c.Data != null)
    //            .Select(c => c.Data!)
    //            .ToList() ?? new List<RedditPost>();

    //        posts.AddRange(fetchedPosts);
    //    }

    //    // Deduplicate by post ID (hot and new may overlap)
    //    return posts
    //        .GroupBy(p => p.Id)
    //        .Select(g => g.First())
    //        .ToList();
    //}
    private async Task<List<RedditPost>> FetchPostsAsync(
    string subreddit,
    CancellationToken cancellationToken)
    {
        var posts = new List<RedditPost>();

        foreach (var sort in new[] { "hot", "new" })
        {
            // Public JSON endpoint — no auth required
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://www.reddit.com/r/{subreddit}/{sort}.json?limit=50");

            // Reddit requires a descriptive User-Agent — without it requests get blocked
            request.Headers.Add("User-Agent",
                "TradingIntelligence/1.0 (personal research tool; contact: your@email.com)");

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Reddit public feed returned {Status} for r/{Sub}/{Sort}",
                    response.StatusCode, subreddit, sort);
                continue;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var listing = JsonSerializer.Deserialize<RedditListing>(json);

            var fetchedPosts = listing?.Data?.Children?
                .Where(c => c.Data != null)
                .Select(c => c.Data!)
                .ToList() ?? new List<RedditPost>();

            posts.AddRange(fetchedPosts);

            // Public endpoint rate limit — 1 request per second
            await Task.Delay(1100, cancellationToken);
        }

        return posts
            .GroupBy(p => p.Id)
            .Select(g => g.First())
            .ToList();
    }

    // No-op — public endpoint needs no token
    private Task EnsureTokenAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    //private async Task EnsureTokenAsync(CancellationToken cancellationToken)
    //{
    //    // Refresh token if expired or not set
    //    if (!string.IsNullOrEmpty(_accessToken) &&
    //        DateTime.UtcNow < _tokenExpiry.AddMinutes(-5))
    //        return;

    //    var clientId = _config["Reddit:ClientId"]
    //        ?? throw new InvalidOperationException("Reddit:ClientId not configured");
    //    var clientSecret = _config["Reddit:ClientSecret"]
    //        ?? throw new InvalidOperationException("Reddit:ClientSecret not configured");
    //    var username = _config["Reddit:Username"]
    //        ?? throw new InvalidOperationException("Reddit:Username not configured");
    //    var password = _config["Reddit:Password"]
    //        ?? throw new InvalidOperationException("Reddit:Password not configured");
    //    var userAgent = _config["Reddit:UserAgent"] ?? "TradingIntelligence/1.0";

    //    var credentials = Convert.ToBase64String(
    //        Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

    //    var tokenRequest = new HttpRequestMessage(
    //        HttpMethod.Post,
    //        "https://www.reddit.com/api/v1/access_token");

    //    tokenRequest.Headers.Authorization =
    //        new AuthenticationHeaderValue("Basic", credentials);
    //    tokenRequest.Headers.Add("User-Agent", userAgent);

    //    tokenRequest.Content = new FormUrlEncodedContent(new[]
    //    {
    //        new KeyValuePair<string, string>("grant_type", "password"),
    //        new KeyValuePair<string, string>("username", username),
    //        new KeyValuePair<string, string>("password", password),
    //    });

    //    var response = await _httpClient.SendAsync(tokenRequest, cancellationToken);
    //    response.EnsureSuccessStatusCode();

    //    var json = await response.Content.ReadAsStringAsync(cancellationToken);
    //    var token = JsonSerializer.Deserialize<RedditTokenResponse>(json)
    //        ?? throw new InvalidOperationException("Failed to deserialise Reddit token response");

    //    _accessToken = token.AccessToken;
    //    _tokenExpiry = DateTime.UtcNow.AddSeconds(token.ExpiresIn);

    //    _logger.LogInformation("Reddit OAuth token obtained — expires at {Expiry}",
    //        _tokenExpiry.ToString("HH:mm:ss"));
    //}
}
