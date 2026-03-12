using Microsoft.Extensions.Logging;
using Quartz;
using TradingIntelligence.Infrastructure.Collectors;

namespace TradingIntelligence.Infrastructure.Jobs;

[DisallowConcurrentExecution]
public class GoogleTrendsCollectorJob : IJob
{
    private readonly GoogleTrendsCollector _collector;
    private readonly ILogger<GoogleTrendsCollectorJob> _logger;

    public GoogleTrendsCollectorJob(
        GoogleTrendsCollector collector,
        ILogger<GoogleTrendsCollectorJob> logger)
    {
        _collector = collector;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("GoogleTrendsCollectorJob firing at {Time}",
            DateTime.UtcNow.ToString("HH:mm:ss"));
        await _collector.CollectAsync(context.CancellationToken);
    }
}