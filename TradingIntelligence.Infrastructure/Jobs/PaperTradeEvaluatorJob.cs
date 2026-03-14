using Microsoft.Extensions.Logging;
using Quartz;
using TradingIntelligence.Core.Interfaces;

namespace TradingIntelligence.Infrastructure.Jobs;

public class PaperTradeEvaluatorJob : IJob
{
    private readonly IPaperTradeService _service;
    private readonly ILogger<PaperTradeEvaluatorJob> _logger;

    public PaperTradeEvaluatorJob(IPaperTradeService service,
        ILogger<PaperTradeEvaluatorJob> logger)
    {
        _service = service;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("PaperTradeEvaluatorJob starting at {Time}", DateTime.UtcNow);
        await _service.EvaluateOpenTradesAsync();
        _logger.LogInformation("PaperTradeEvaluatorJob complete");
    }
}
