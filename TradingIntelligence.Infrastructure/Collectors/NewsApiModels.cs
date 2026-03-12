using System.Text.Json.Serialization;

namespace TradingIntelligence.Infrastructure.Collectors;

public class NewsApiResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("totalResults")]
    public int TotalResults { get; set; }

    [JsonPropertyName("articles")]
    public List<NewsApiArticle> Articles { get; set; } = new();
}

public class NewsApiArticle
{
    [JsonPropertyName("source")]
    public NewsApiSource? Source { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("publishedAt")]
    public string PublishedAt { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    public DateTime PublishedAtParsed =>
        DateTime.TryParse(PublishedAt, out var dt)
            ? dt.ToUniversalTime()
            : DateTime.UtcNow;
}

public class NewsApiSource
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}