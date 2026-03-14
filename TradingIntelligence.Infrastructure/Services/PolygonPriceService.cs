using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using TradingIntelligence.Core.Interfaces;

namespace TradingIntelligence.Infrastructure.Services;

public class PolygonPriceService : IPolygonPriceService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<PolygonPriceService> _logger;
    private static readonly SemaphoreSlim _throttle = new(1, 1);
    private static DateTime _lastCall = DateTime.MinValue;
    private const int DelayMs = 13000;

    public PolygonPriceService(IHttpClientFactory factory, IConfiguration config, ILogger<PolygonPriceService> logger)
    {
        _http = factory.CreateClient();
        _apiKey = config["Polygon:ApiKey"]!;
        _logger = logger;
    }

    public async Task<decimal?> GetLastPriceAsync(string ticker)
    {
        await _throttle.WaitAsync();
        try
        {
            var wait = DelayMs - (int)(DateTime.UtcNow - _lastCall).TotalMilliseconds;
            if (wait > 0) await Task.Delay(wait);

            var url = $"https://api.polygon.io/v2/last/trade/{ticker}?apiKey={_apiKey}";
            var response = await _http.GetFromJsonAsync<PolygonLastTradeResponse>(url);
            _lastCall = DateTime.UtcNow;

            if (response?.Results is null || response.Results.P <= 0)
            {
                _logger.LogWarning("Polygon price response invalid for {Ticker}", ticker);
                return null;
            }

            return response.Results.P;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Polygon price lookup failed for {Ticker}", ticker);
            return null;
        }
        finally
        {
            _throttle.Release();
        }
    }
}

public record PolygonLastTradeResponse(PolygonTradeResult? Results);
public record PolygonTradeResult(decimal P, long T); // P = price, T = timestamp
