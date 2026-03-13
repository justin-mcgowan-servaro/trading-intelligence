using Microsoft.Extensions.Logging;
using Quartz;
using TradingIntelligence.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace TradingIntelligence.Infrastructure.Jobs;

public class OtpCleanupJob : IJob
{
    private readonly AppDbContext _db;
    private readonly ILogger<OtpCleanupJob> _logger;

    public OtpCleanupJob(AppDbContext db, ILogger<OtpCleanupJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var expired = await _db.OtpCodes
            .Where(o => o.ExpiresAt < DateTime.UtcNow || o.Used)
            .ToListAsync(context.CancellationToken);

        if (expired.Count == 0) return;

        _db.OtpCodes.RemoveRange(expired);
        await _db.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation("OTP cleanup: removed {Count} expired/used codes", expired.Count);
    }
}
