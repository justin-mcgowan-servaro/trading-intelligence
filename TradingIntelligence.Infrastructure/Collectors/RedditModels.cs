using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace TradingIntelligence.Infrastructure.Collectors;

// Reddit OAuth token response
public class RedditTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

// Reddit listing response wrapper
public class RedditListing
{
    [JsonPropertyName("data")]
    public RedditListingData? Data { get; set; }
}

public class RedditListingData
{
    [JsonPropertyName("children")]
    public List<RedditChild>? Children { get; set; }
}

public class RedditChild
{
    [JsonPropertyName("data")]
    public RedditPost? Data { get; set; }
}

public class RedditPost
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("selftext")]
    public string Selftext { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("num_comments")]
    public int NumComments { get; set; }

    [JsonPropertyName("upvote_ratio")]
    public double UpvoteRatio { get; set; }

    [JsonPropertyName("created_utc")]
    public double CreatedUtc { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("link_flair_text")]
    public string? Flair { get; set; }

    public DateTime CreatedDateTime =>
        DateTimeOffset.FromUnixTimeSeconds((long)CreatedUtc).UtcDateTime;
}
