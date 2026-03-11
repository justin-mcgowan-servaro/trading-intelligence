using System;
using System.Collections.Generic;
using System.Text;
using TradingIntelligence.Core.Enums;

namespace TradingIntelligence.Core.Models;

public class MomentumScoreResult
{
    public string TickerSymbol { get; set; } = string.Empty;
    public decimal TotalScore { get; set; }
    public decimal RedditScore { get; set; }
    public decimal NewsScore { get; set; }
    public decimal VolumeScore { get; set; }
    public decimal OptionsScore { get; set; }
    public decimal SentimentScore { get; set; }
    public TradeBias TradeBias { get; set; }
    public string Confidence { get; set; } = string.Empty;
    public string SignalSummary { get; set; } = string.Empty;
    public string AiAnalysis { get; set; } = string.Empty;
    public MarketSession Session { get; set; }
    public DateTime ScoredAt { get; set; }
    public string ScoredAtSast { get; set; } = string.Empty;
}
