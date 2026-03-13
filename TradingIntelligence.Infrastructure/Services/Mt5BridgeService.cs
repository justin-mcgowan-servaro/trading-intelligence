using System.Net.Http.Json;
using TradingIntelligence.Core.Enums;
using TradingIntelligence.Core.Interfaces;
using TradingIntelligence.Core.Models;

namespace TradingIntelligence.Infrastructure.Services;

public class Mt5BridgeService : IMt5BridgeService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<Mt5BridgeService> _logger;

    public Mt5BridgeService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<Mt5BridgeService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<long?> PlaceOrderAsync(string symbol, TradeDirection direction, decimal lots)
    {
        try
        {
            var baseUrl = GetBaseUrl();
            if (string.IsNullOrEmpty(baseUrl)) return null;

            using var http = CreateClient();
            var response = await http.PostAsJsonAsync(
                $"{baseUrl}/order",
                new { symbol, direction = direction.ToString(), lots });

            if (!response.IsSuccessStatusCode) return null;

            var payload = await response.Content.ReadFromJsonAsync<OrderResponse>();
            return payload?.Ticket;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MT5 place order failed for {Symbol}", symbol);
            return null;
        }
    }

    public async Task<Mt5PositionResult?> GetPositionAsync(long ticket)
    {
        try
        {
            var baseUrl = GetBaseUrl();
            if (string.IsNullOrEmpty(baseUrl)) return null;

            using var http = CreateClient();
            return await http.GetFromJsonAsync<Mt5PositionResult>($"{baseUrl}/positions/{ticket}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MT5 get position failed for ticket {Ticket}", ticket);
            return null;
        }
    }

    public async Task<bool> CloseOrderAsync(long ticket)
    {
        try
        {
            var baseUrl = GetBaseUrl();
            if (string.IsNullOrEmpty(baseUrl)) return false;

            using var http = CreateClient();
            var response = await http.DeleteAsync($"{baseUrl}/order/{ticket}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MT5 close order failed for ticket {Ticket}", ticket);
            return false;
        }
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        return client;
    }

    private string? GetBaseUrl()
    {
        var baseUrl = _configuration["Mt5Bridge:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogWarning("Mt5Bridge:BaseUrl is not configured");
            return null;
        }

        return baseUrl.TrimEnd('/');
    }

    private sealed class OrderResponse
    {
        public long Ticket { get; set; }
    }
}
