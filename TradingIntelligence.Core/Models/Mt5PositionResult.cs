namespace TradingIntelligence.Core.Models;

public record Mt5PositionResult(
    long Ticket,
    decimal CurrentPrice,
    decimal Profit,
    bool IsOpen);
