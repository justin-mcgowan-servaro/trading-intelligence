public class PolygonPriceService : IPolygonPriceService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private static readonly SemaphoreSlim _throttle = new(1, 1);
    private static DateTime _lastCall = DateTime.MinValue;
    private const int DelayMs = 13000;

    public PolygonPriceService(IHttpClientFactory factory, IConfiguration config)
    {
        _http = factory.CreateClient();
        _apiKey = config["Polygon:ApiKey"]!;
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

            return response?.Results?.P;
        }
        catch (Exception ex)
        {
            // log and return null — don't crash the evaluator
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
