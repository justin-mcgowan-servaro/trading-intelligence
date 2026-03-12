using Microsoft.Extensions.Logging;
using Quartz;
using TradingIntelligence.Infrastructure.Collectors;

namespace TradingIntelligence.Infrastructure.Jobs;

[DisallowConcurrentExecution]
public class PolygonCollectorJob : IJob
{
    private readonly PolygonCollector _collector;
    private readonly ILogger<PolygonCollectorJob> _logger;

    public PolygonCollectorJob(
        PolygonCollector collector,
        ILogger<PolygonCollectorJob> logger)
    {
        _collector = collector;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("PolygonCollectorJob firing at {Time}",
            DateTime.UtcNow.ToString("HH:mm:ss"));
        await _collector.CollectAsync(context.CancellationToken);
    }
}