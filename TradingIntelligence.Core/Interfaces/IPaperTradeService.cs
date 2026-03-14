using TradingIntelligence.Core.Entities;

namespace TradingIntelligence.Core.Interfaces;

public interface IPaperTradeService
{
    Task TryCreateAutoTradeAsync(MomentumScore score);
    Task EvaluateOpenTradesAsync();
    Task UpdateSignalAccuracyAsync(PaperTrade trade);
}
