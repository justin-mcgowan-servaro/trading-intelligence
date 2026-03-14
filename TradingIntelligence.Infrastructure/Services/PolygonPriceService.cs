using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net;
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

    public PolygonPriceService(
        IHttpClientFactory factory,
        IConfiguration config,
        ILogger<PolygonPriceService> logger)
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
            if (wait > 0)
                await Task.Delay(wait);

            var url = $"https://api.polygon.io/v2/last/trade/{ticker}?apiKey={_apiKey}";
            using var response = await _http.GetAsync(url);

            _lastCall = DateTime.UtcNow;

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogWarning(
                    "Polygon price lookup forbidden for {Ticker}. API key may lack entitlement for /v2/last/trade.",
                    ticker);
                return null;
            }

            if ((int)response.StatusCode == 429)
            {
                _logger.LogWarning(
                    "Polygon price lookup rate-limited for {Ticker}",
                    ticker);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    "Polygon price lookup failed for {Ticker}. Status: {Status}. Body: {Body}",
                    ticker,
                    (int)response.StatusCode,
                    body);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<PolygonLastTradeResponse>();

            if (payload?.Results is null || payload.Results.P <= 0)
            {
                _logger.LogWarning("Polygon price response invalid for {Ticker}", ticker);
                return null;
            }

            return payload.Results.P;
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
public record PolygonTradeResult(decimal P, long T);