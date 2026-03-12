using Microsoft.AspNetCore.SignalR;
using TradingIntelligence.Api.Hubs;
using TradingIntelligence.Core.Interfaces;
using TradingIntelligence.Core.Models;

namespace TradingIntelligence.Api.Services;

public class SignalRNotifier : IRealtimeNotifier
{
    private readonly IHubContext<MomentumHub> _hubContext;

    public SignalRNotifier(IHubContext<MomentumHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyMomentumUpdate(
        MomentumScoreResult result,
        CancellationToken cancellationToken = default)
    {
        // Broadcast to all connected clients
        await _hubContext.Clients.All.SendAsync(
            "MomentumUpdate", result, cancellationToken);

        // Also send to ticker-specific group
        await _hubContext.Clients
            .Group($"ticker-{result.TickerSymbol}")
            .SendAsync("TickerUpdate", result, cancellationToken);
    }
}