using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradingIntelligence.Core.Enums;
using TradingIntelligence.Core.Models;
using TradingIntelligence.Infrastructure.Helpers;

namespace TradingIntelligence.Infrastructure.Services;

public class TelegramAlertService
{
    private readonly IConfiguration _config;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<TelegramAlertService> _logger;
    private readonly HttpClient _httpClient;

    private const string AlertsKey = "alerts:recent";
    private const decimal AlertThreshold = 60m;

    public TelegramAlertService(
        IConfiguration config,
        IConnectionMultiplexer redis,
        ILogger<TelegramAlertService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _redis = redis;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("Telegram");
    }

    public async Task SendScoreAlertAsync(
        MomentumScoreResult result,
        CancellationToken cancellationToken = default)
    {
        if (result.TotalScore < AlertThreshold) return;

        var botToken = _config["Telegram:BotToken"];
        var chatId = _config["Telegram:ChatId"];

        if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(chatId))
        {
            _logger.LogWarning("Telegram not configured — skipping alert");
            return;
        }

        var db = _redis.GetDatabase();

        // Dedup — one alert per ticker per 2 hours
        var dedupKey = $"telegram:alert:{result.TickerSymbol}:{DateTime.UtcNow:yyyyMMddHH}";
        if (await db.KeyExistsAsync(dedupKey)) return;

        var biasEmoji = result.TradeBias switch
        {
            TradeBias.Long => "🟢",
            TradeBias.Short => "🔴",
            TradeBias.Watch => "🟡",
            _ => "⚪"
        };

        var confidenceEmoji = result.Confidence switch
        {
            "HIGH" => "🔥",
            "MEDIUM" => "⚡",
            _ => "📊"
        };

        var message = $"""
            {confidenceEmoji} *SERVARO TRADING ALERT*

            *{result.TickerSymbol}* — Score: *{result.TotalScore}/100*
            {biasEmoji} Bias: *{result.TradeBias}* | Confidence: *{result.Confidence}*
            📍 Session: {MarketSessionHelper.SessionDisplayName(result.Session)}
            🕐 Time: {result.ScoredAtSast}

            📊 *Signal Breakdown:*
            Reddit:    {result.RedditScore}/20
            News:      {result.NewsScore}/20
            Volume:    {result.VolumeScore}/20
            Options:   {result.OptionsScore}/20
            Sentiment: {result.SentimentScore}/20

            📋 Signals: _{result.SignalSummary}_

            🔗 [View Dashboard](https://servaro.co.za)
            """;

        var sent = await SendMessageAsync(botToken, chatId, message, cancellationToken);

        if (sent)
        {
            await db.StringSetAsync(dedupKey, "1", TimeSpan.FromHours(2));
            await CacheAlertAsync(db, result);

            _logger.LogInformation(
                "Telegram alert sent for {Ticker} — {Score}/100",
                result.TickerSymbol, result.TotalScore);
            return;
        }

        _logger.LogWarning(
            "Telegram alert failed for {Ticker} — not caching alert and not setting dedup",
            result.TickerSymbol);
    }

    public async Task SendMorningBriefingAsync(
        string briefingText,
        CancellationToken cancellationToken = default)
    {
        var botToken = _config["Telegram:BotToken"];
        var chatId = _config["Telegram:ChatId"];

        if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(chatId))
            return;

        var header = $"""
            ☀️ *SERVARO MORNING BRIEFING*
            {DateTime.UtcNow.AddHours(2):dddd, dd MMMM yyyy} — 06:00 SAST

            """;

        var sent = await SendMessageAsync(
            botToken, chatId,
            header + briefingText,
            cancellationToken);

        if (sent)
        {
            _logger.LogInformation("Morning briefing sent to Telegram");
            return;
        }

        _logger.LogWarning("Morning briefing failed to send to Telegram");
    }

    private async Task<bool> SendMessageAsync(
        string botToken,
        string chatId,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://api.telegram.org/bot{botToken}/sendMessage";

            var payload = JsonSerializer.Serialize(new
            {
                chat_id = chatId,
                text = message,
                parse_mode = "Markdown",
                disable_web_page_preview = false
            });

            var content = new StringContent(
                payload, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                url, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Telegram API error: {Status} — {Error}",
                response.StatusCode, error);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram message send failed with exception");
            return false;
        }
    }

    private static async Task CacheAlertAsync(
        IDatabase db,
        MomentumScoreResult result)
    {
        var alert = JsonSerializer.Serialize(new
        {
            result.TickerSymbol,
            result.TotalScore,
            TradeBias = result.TradeBias.ToString(),
            result.Confidence,
            result.SignalSummary,
            ScoredAtSast = result.ScoredAtSast,
            AlertedAt = MarketSessionHelper.ToSast(DateTime.UtcNow)
        });

        await db.ListLeftPushAsync(AlertsKey, alert);
        await db.ListTrimAsync(AlertsKey, 0, 19);
        await db.KeyExpireAsync(AlertsKey, TimeSpan.FromHours(24));
    }

    public static async Task<List<string>> GetRecentAlertsAsync(IDatabase db)
    {
        var alerts = await db.ListRangeAsync(AlertsKey, 0, 19);
        return alerts.Select(a => a.ToString()).ToList();
    }
}