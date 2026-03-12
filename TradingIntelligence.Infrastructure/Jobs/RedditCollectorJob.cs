using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;
using Quartz;
using TradingIntelligence.Infrastructure.Collectors;

namespace TradingIntelligence.Infrastructure.Jobs;

[DisallowConcurrentExecution]
public class RedditCollectorJob : IJob
{
    private readonly RedditCollector _collector;
    private readonly ILogger<RedditCollectorJob> _logger;

    public RedditCollectorJob(
        RedditCollector collector,
        ILogger<RedditCollectorJob> logger)
    {
        _collector = collector;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation(
            "RedditCollectorJob firing at {Time}",
            DateTime.UtcNow.ToString("HH:mm:ss"));

        await _collector.CollectAsync(context.CancellationToken);
    }
}
