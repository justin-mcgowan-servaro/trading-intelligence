using Microsoft.AspNetCore.SignalR;

namespace TradingIntelligence.Api.Hubs;

public class MomentumHub : Hub
{
    private readonly ILogger<MomentumHub> _logger;

    public MomentumHub(ILogger<MomentumHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation(
            "Client connected to MomentumHub: {ConnectionId}",
            Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation(
            "Client disconnected from MomentumHub: {ConnectionId}",
            Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    // Client can call this to subscribe to specific tickers
    public async Task SubscribeTicker(string ticker)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId,
            $"ticker-{ticker.ToUpperInvariant()}");

        _logger.LogInformation(
            "Client {ConnectionId} subscribed to {Ticker}",
            Context.ConnectionId, ticker);
    }

    public async Task UnsubscribeTicker(string ticker)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId,
            $"ticker-{ticker.ToUpperInvariant()}");
    }
}