using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;
using Quartz;
using TradingIntelligence.Infrastructure.Collectors;

namespace TradingIntelligence.Infrastructure.Jobs;

[DisallowConcurrentExecution]
public class StockTwitsCollectorJob : IJob
{
    private readonly StockTwitsCollector _collector;
    private readonly ILogger<StockTwitsCollectorJob> _logger;

    public StockTwitsCollectorJob(
        StockTwitsCollector collector,
        ILogger<StockTwitsCollectorJob> logger)
    {
        _collector = collector;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation(
            "StockTwitsCollectorJob firing at {Time}",
            DateTime.UtcNow.ToString("HH:mm:ss"));

        await _collector.CollectAsync(context.CancellationToken);
    }
}
