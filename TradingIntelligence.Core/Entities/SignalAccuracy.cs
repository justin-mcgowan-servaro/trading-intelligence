namespace TradingIntelligence.Core.Entities;

public class SignalAccuracy
{
    public int Id { get; set; }
    public string TickerSymbol { get; set; } = string.Empty;
    public int TotalTrades { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Breakevens { get; set; }
    public decimal WinRate { get; set; }
    public decimal AvgPnlPercent { get; set; }
    public decimal AvgScoreAtEntry { get; set; }
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
}
