using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using Quartz;
using StackExchange.Redis;
using TradingIntelligence.Core.Prompts;
using TradingIntelligence.Infrastructure.Data;
using TradingIntelligence.Infrastructure.Services;
using TradingIntelligence.Infrastructure.Helpers;
using TradingIntelligence.Infrastructure.Collectors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace TradingIntelligence.Infrastructure.Jobs;

[DisallowConcurrentExecution]
public class MorningBriefingJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TelegramAlertService _telegram;
    private readonly IConnectionMultiplexer _redis;
    private readonly IConfiguration _config;
    private readonly ILogger<MorningBriefingJob> _logger;

    public MorningBriefingJob(
        IServiceScopeFactory scopeFactory,
        TelegramAlertService telegram,
        IConnectionMultiplexer redis,
        IConfiguration config,
        ILogger<MorningBriefingJob> logger)
    {
        _scopeFactory = scopeFactory;
        _telegram = telegram;
        _redis = redis;
        _config = config;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("MorningBriefingJob firing at {Time}",
            MarketSessionHelper.ToSast(DateTime.UtcNow));

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Get top 5 scoring tickers from last 12 hours
            var since = DateTime.UtcNow.AddHours(-12);
            var topScores = await db.MomentumScores
                .Where(s => s.ScoredAt >= since)
                .GroupBy(s => s.TickerSymbol)
                .Select(g => g.OrderByDescending(s => s.TotalScore).First())
                .OrderByDescending(s => s.TotalScore)
                .Take(5)
                .ToListAsync(context.CancellationToken);

            if (!topScores.Any())
            {
                _logger.LogInformation(
                    "No scores in last 12 hours — skipping briefing");
                return;
            }

            // Get Fear & Greed context
            var redisDb = _redis.GetDatabase();
            var fearGreed = await FearGreedCollector
                .GetCachedAsync(redisDb);

            // Build briefing payload for OpenAI
            var scoresSummary = string.Join("\n", topScores.Select(s =>
                $"  {s.TickerSymbol}: {s.TotalScore}/100 " +
                $"({s.TradeBias}) — {s.SignalSummary}"));

            var payload = $"""
                MORNING BRIEFING REQUEST — {MarketSessionHelper.ToSast(DateTime.UtcNow)}

                Generate a concise morning briefing for traders covering:
                1. Market context from Fear & Greed data
                2. Top 5 overnight momentum opportunities
                3. Key risk factors to watch today
                4. Recommended focus for the NY open at 15:30 SAST

                FEAR_AND_GREED_DATA:
                {fearGreed}

                TOP_5_OVERNIGHT_SCORES:
                {scoresSummary}

                Format as a clean, actionable briefing under 400 words.
                Use plain text — no markdown symbols.
                """;

            var apiKey = _config["OpenAI:ApiKey"];
            if (string.IsNullOrEmpty(apiKey)) return;

            var client = new ChatClient(
                model: _config["OpenAI:Model"] ?? "gpt-5-nano",
                apiKey: apiKey);

            var messages = new List<ChatMessage>
            {
                ChatMessage.CreateSystemMessage(
                    SystemPrompts.TradingIntelligence),
                ChatMessage.CreateUserMessage(payload)
            };

            var completion = await client.CompleteChatAsync(
                messages,
                cancellationToken: context.CancellationToken);

            var briefing = completion.Value.Content[0].Text;

            await _telegram.SendMorningBriefingAsync(
                briefing, context.CancellationToken);

            _logger.LogInformation("Morning briefing generated and sent");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating morning briefing");
        }
    }
}