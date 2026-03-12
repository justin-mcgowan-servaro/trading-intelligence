using System;
using System.Collections.Generic;
using System.Text;
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradingIntelligence.Core.Models;
using TradingIntelligence.Infrastructure.Helpers;

namespace TradingIntelligence.Infrastructure.Services;

public class SignalAggregatorService : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SignalAggregatorService> _logger;

    // In-memory buffer: ticker symbol → list of raw signals in last 24hrs
    private readonly ConcurrentDictionary<string, List<RawSignalEvent>> _buffer = new();

    // Track which tickers have already triggered scoring in current window
    // to avoid calling OpenAI repeatedly for the same data
    private readonly ConcurrentDictionary<string, DateTime> _lastScored = new();

    // Minimum time between scoring the same ticker (30 minutes)
    private static readonly TimeSpan ScoringCooldown = TimeSpan.FromMinutes(30);

    public SignalAggregatorService(
        IConnectionMultiplexer redis,
        ILogger<SignalAggregatorService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SignalAggregatorService starting — subscribing to raw-signals");

        var subscriber = _redis.GetSubscriber();

        await subscriber.SubscribeAsync(
            RedisChannel.Literal("raw-signals"),
            (channel, message) => HandleSignal(message, stoppingToken));

        _logger.LogInformation("Subscribed to Redis channel: raw-signals");

        // Background cleanup — prune signals older than 24hrs every hour
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                PruneOldSignals();
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SignalAggregator cleanup loop");
            }
        }
    }

    private void HandleSignal(RedisValue message, CancellationToken cancellationToken)
    {
        try
        {
            if (message.IsNullOrEmpty) return;

            var signal = JsonSerializer.Deserialize<RawSignalEvent>(message.ToString());
            if (signal == null) return;

            foreach (var ticker in signal.Tickers)
            {
                if (!TickerExtractor.IsValidTicker(ticker)) continue;

                // Add to buffer
                _buffer.AddOrUpdate(
                    ticker,
                    _ => new List<RawSignalEvent> { signal },
                    (_, existing) =>
                    {
                        lock (existing) { existing.Add(signal); }
                        return existing;
                    });

                // Check if we have enough signal types to score
                var tickerSignals = _buffer[ticker];
                int distinctSignalTypes;

                lock (tickerSignals)
                {
                    // Only count signals from last 24 hours
                    var recentSignals = tickerSignals
                        .Where(s => s.DetectedAt > DateTime.UtcNow.AddHours(-24))
                        .ToList();

                    distinctSignalTypes = recentSignals
                        .Select(s => s.SignalType)
                        .Distinct()
                        .Count();
                }

                if (distinctSignalTypes >= 1)
                {
                    // Check cooldown — don't score same ticker more than once per 30 mins
                    if (_lastScored.TryGetValue(ticker, out var lastTime) &&
                        DateTime.UtcNow - lastTime < ScoringCooldown)
                        continue;

                    _lastScored[ticker] = DateTime.UtcNow;

                    _logger.LogInformation(
                        "Ticker {Ticker} has {Count} signal types — publishing to scored-signals",
                        ticker, distinctSignalTypes);

                    // Publish ticker to the scoring channel
                    // The scoring service picks this up and calls OpenAI
                    var pub = _redis.GetSubscriber();
                    pub.Publish(
                        RedisChannel.Literal("scored-signals"),
                        ticker);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling signal message");
        }
    }

    private void PruneOldSignals()
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);
        int pruned = 0;

        foreach (var key in _buffer.Keys)
        {
            if (_buffer.TryGetValue(key, out var signals))
            {
                lock (signals)
                {
                    int before = signals.Count;
                    signals.RemoveAll(s => s.DetectedAt < cutoff);
                    pruned += before - signals.Count;
                }

                // Remove ticker entirely if no recent signals
                if (signals.Count == 0)
                    _buffer.TryRemove(key, out _);
            }
        }

        if (pruned > 0)
            _logger.LogInformation(
                "Pruned {Count} signals older than 24hrs from buffer", pruned);
    }

    /// <summary>
    /// Get current buffer snapshot for a ticker — used by the scoring service
    /// </summary>
    public List<RawSignalEvent> GetTickerSignals(string ticker)
    {
        if (!_buffer.TryGetValue(ticker, out var signals))
            return new List<RawSignalEvent>();

        lock (signals)
        {
            return signals
                .Where(s => s.DetectedAt > DateTime.UtcNow.AddHours(-24))
                .ToList();
        }
    }

    /// <summary>
    /// Get all tickers currently in the buffer with their signal counts
    /// </summary>
    public Dictionary<string, int> GetBufferSummary()
    {
        return _buffer.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Count);
    }
}
