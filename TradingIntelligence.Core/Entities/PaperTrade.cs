using TradingIntelligence.Core.Enums;

namespace TradingIntelligence.Core.Entities;

public class PaperTrade
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string TickerSymbol { get; set; } = string.Empty;
    public int MomentumScoreId { get; set; }
    public MomentumScore MomentumScore { get; set; } = null!;
    public decimal EntryPrice { get; set; }
    public TradeDirection Direction { get; set; }
    public TradeBias TradeBias { get; set; }
    public decimal TotalScoreAtEntry { get; set; }
    public TradeStatus Status { get; set; } = TradeStatus.Open;
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal? PnlPoints { get; set; }
    public decimal? PnlPercent { get; set; }
    public TradeOutcome Outcome { get; set; } = TradeOutcome.Pending;
    public string? Notes { get; set; }
}
