using Microsoft.Extensions.Logging;
using Quartz;
using TradingIntelligence.Infrastructure.Collectors;

namespace TradingIntelligence.Infrastructure.Jobs;

[DisallowConcurrentExecution]
public class FearGreedCollectorJob : IJob
{
    private readonly FearGreedCollector _collector;
    private readonly ILogger<FearGreedCollectorJob> _logger;

    public FearGreedCollectorJob(
        FearGreedCollector collector,
        ILogger<FearGreedCollectorJob> logger)
    {
        _collector = collector;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("FearGreedCollectorJob firing at {Time}",
            DateTime.UtcNow.ToString("HH:mm:ss"));
        await _collector.CollectAsync(context.CancellationToken);
    }
}