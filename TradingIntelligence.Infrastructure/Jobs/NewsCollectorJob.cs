using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;
using Quartz;
using TradingIntelligence.Infrastructure.Collectors;

namespace TradingIntelligence.Infrastructure.Jobs;

[DisallowConcurrentExecution]
public class NewsCollectorJob : IJob
{
    private readonly NewsCollector _collector;
    private readonly ILogger<NewsCollectorJob> _logger;

    public NewsCollectorJob(
        NewsCollector collector,
        ILogger<NewsCollectorJob> logger)
    {
        _collector = collector;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("NewsCollectorJob firing at {Time}",
            DateTime.UtcNow.ToString("HH:mm:ss"));

        await _collector.CollectAsync(context.CancellationToken);
    }
}
