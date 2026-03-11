using System;
using System.Collections.Generic;
using System.Text;
using TradingIntelligence.Core.Enums;

namespace TradingIntelligence.Core.Entities;

public class SignalEvent
{
    public int Id { get; set; }
    public string TickerSymbol { get; set; } = string.Empty;
    public SignalType SignalType { get; set; }
    public decimal SignalScore { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? RawText { get; set; }
    public decimal SentimentScore { get; set; }
    public string? RawData { get; set; }  // JSON blob
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Ticker? Ticker { get; set; }
}