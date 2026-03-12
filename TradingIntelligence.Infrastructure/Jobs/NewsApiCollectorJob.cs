using Microsoft.Extensions.Logging;
using Quartz;
using TradingIntelligence.Infrastructure.Collectors;

namespace TradingIntelligence.Infrastructure.Jobs;

[DisallowConcurrentExecution]
public class NewsApiCollectorJob : IJob
{
    private readonly NewsApiCollector _collector;
    private readonly ILogger<NewsApiCollectorJob> _logger;

    public NewsApiCollectorJob(
        NewsApiCollector collector,
        ILogger<NewsApiCollectorJob> logger)
    {
        _collector = collector;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("NewsApiCollectorJob firing at {Time}",
            DateTime.UtcNow.ToString("HH:mm:ss"));
        await _collector.CollectAsync(context.CancellationToken);
    }
}