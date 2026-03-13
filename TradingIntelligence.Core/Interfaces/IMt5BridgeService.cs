using TradingIntelligence.Core.Enums;
using TradingIntelligence.Core.Models;

namespace TradingIntelligence.Core.Interfaces;

public interface IMt5BridgeService
{
    Task<long?> PlaceOrderAsync(string symbol, TradeDirection direction, decimal lots);
    Task<Mt5PositionResult?> GetPositionAsync(long ticket);
    Task<bool> CloseOrderAsync(long ticket);
}
