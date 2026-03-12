using System;
using System.Collections.Generic;
using System.Text;
namespace TradingIntelligence.Infrastructure.Collectors;

public class NewsItem
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public double CredibilityWeight { get; set; } = 1.0;
}