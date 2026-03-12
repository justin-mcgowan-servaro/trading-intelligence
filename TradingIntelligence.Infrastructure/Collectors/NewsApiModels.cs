using System.Text.Json.Serialization;

public class NewsApiResponse
{
    [JsonPropertyName("totalArticles")]  // GNews uses totalArticles not totalResults
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
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]  // GNews has url instead of id
    public string? Url { get; set; }
}