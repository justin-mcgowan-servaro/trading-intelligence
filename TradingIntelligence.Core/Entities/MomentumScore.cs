using System;
using System.Collections.Generic;
using System.Text;
using TradingIntelligence.Core.Enums;

namespace TradingIntelligence.Core.Entities;

public class MomentumScore
{
    public int Id { get; set; }
    public string TickerSymbol { get; set; } = string.Empty;
    public decimal TotalScore { get; set; }
    public decimal RedditScore { get; set; }
    public decimal NewsScore { get; set; }
    public decimal VolumeScore { get; set; }
    public decimal OptionsScore { get; set; }
    public decimal SentimentScore { get; set; }
    public TradeBias TradeBias { get; set; }
    public string? SignalSummary { get; set; }
    public string? AiAnalysis { get; set; }  // Full OpenAI response
    public string? TradeSetup { get; set; }
    public string? RiskFactors { get; set; }
    public MarketSession Session { get; set; }
    public DateTime ScoredAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Ticker? Ticker { get; set; }
}
