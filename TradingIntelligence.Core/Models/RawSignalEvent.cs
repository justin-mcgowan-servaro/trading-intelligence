using System;
using System.Collections.Generic;
using System.Text;
using TradingIntelligence.Core.Enums;

namespace TradingIntelligence.Core.Models;

public class RawSignalEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public SignalType SignalType { get; set; }
    public List<string> Tickers { get; set; } = new();
    public string Source { get; set; } = string.Empty;
    public string? RawText { get; set; }
    public decimal SentimentScore { get; set; }
    public string? RawData { get; set; }
    public int AuthorKarma { get; set; }
    public int AccountAgeMonths { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}
