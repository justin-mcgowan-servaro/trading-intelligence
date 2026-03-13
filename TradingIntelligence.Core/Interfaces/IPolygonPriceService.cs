namespace TradingIntelligence.Core.Interfaces;

public interface IPolygonPriceService
{
    Task<decimal?> GetLastPriceAsync(string ticker);
}
