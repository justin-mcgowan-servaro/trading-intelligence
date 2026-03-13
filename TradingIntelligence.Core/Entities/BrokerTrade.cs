using TradingIntelligence.Core.Enums;

namespace TradingIntelligence.Core.Entities;

public class BrokerTrade
{
    public int Id { get; set; }
    public int PaperTradeId { get; set; }
    public PaperTrade PaperTrade { get; set; } = null!;
    public long Mt5Ticket { get; set; }
    public string Mt5Symbol { get; set; } = string.Empty;
    public decimal LotSize { get; set; }
    public decimal? FilledPrice { get; set; }
    public decimal? CurrentPrice { get; set; }
    public BrokerStatus BrokerStatus { get; set; } = BrokerStatus.Pending;
    public TradeDirection Direction { get; set; }
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public DateTime? SyncedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
