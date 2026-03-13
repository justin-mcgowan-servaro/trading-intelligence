using Microsoft.Extensions.Logging;
using Quartz;
using TradingIntelligence.Infrastructure.Collectors;

namespace TradingIntelligence.Infrastructure.Jobs;

[DisallowConcurrentExecution]
public class OptionsCollectorJob : IJob
{
    private readonly OptionsCollector _collector;
    private readonly ILogger<OptionsCollectorJob> _logger;

    public OptionsCollectorJob(
        OptionsCollector collector,
        ILogger<OptionsCollectorJob> logger)
    {
        _collector = collector;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("OptionsCollectorJob firing at {Time}",
            DateTime.UtcNow.ToString("HH:mm:ss"));
        await _collector.CollectAsync(context.CancellationToken);
    }
}
