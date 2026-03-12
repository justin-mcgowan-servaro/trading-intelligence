using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;
using Quartz;
using TradingIntelligence.Infrastructure.Collectors;

namespace TradingIntelligence.Infrastructure.Jobs;

[DisallowConcurrentExecution]
public class VolumeCollectorJob : IJob
{
    private readonly VolumeCollector _collector;
    private readonly ILogger<VolumeCollectorJob> _logger;

    public VolumeCollectorJob(
        VolumeCollector collector,
        ILogger<VolumeCollectorJob> logger)
    {
        _collector = collector;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("VolumeCollectorJob firing at {Time}",
            DateTime.UtcNow.ToString("HH:mm:ss"));

        await _collector.CollectAsync(context.CancellationToken);
    }
}