using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace TradingIntelligence.Infrastructure.Collectors;

public class StockTwitsResponse
{
    [JsonPropertyName("symbol")]
    public StockTwitsSymbol? Symbol { get; set; }

    [JsonPropertyName("messages")]
    public List<StockTwitsMessage>? Messages { get; set; }
}

public class StockTwitsSymbol
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("watchlist_count")]
    public int WatchlistCount { get; set; }
}

public class StockTwitsMessage
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("user")]
    public StockTwitsUser? User { get; set; }

    [JsonPropertyName("entities")]
    public StockTwitsEntities? Entities { get; set; }

    [JsonPropertyName("sentiment")]
    public StockTwitsSentiment? Sentiment { get; set; }

    public DateTime CreatedAtParsed =>
        DateTime.TryParse(CreatedAt, out var dt) ? dt.ToUniversalTime() : DateTime.UtcNow;
}

public class StockTwitsUser
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("followers")]
    public int Followers { get; set; }

    [JsonPropertyName("following")]
    public int Following { get; set; }
}

public class StockTwitsEntities
{
    [JsonPropertyName("symbols")]
    public List<StockTwitsEntitySymbol>? Symbols { get; set; }
}

public class StockTwitsEntitySymbol
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
}

public class StockTwitsSentiment
{
    [JsonPropertyName("basic")]
    public string? Basic { get; set; }  // "Bullish" or "Bearish" or null
}
