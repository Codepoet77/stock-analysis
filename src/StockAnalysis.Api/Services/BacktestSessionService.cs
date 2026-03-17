using System.Collections.Concurrent;

namespace StockAnalysis.Api.Services;

/// <summary>
/// Singleton — holds in-memory CancellationTokenSources keyed by userId.
/// Provides instant cancellation without a DB round-trip.
/// </summary>
public class BacktestSessionService
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _sessions = new();

    public CancellationToken CreateSession(string userId)
    {
        // Cancel any existing session for this user
        if (_sessions.TryRemove(userId, out var existing))
            existing.Cancel();

        var cts = new CancellationTokenSource();
        _sessions[userId] = cts;
        return cts.Token;
    }

    public void CancelSession(string userId)
    {
        if (_sessions.TryRemove(userId, out var cts))
            cts.Cancel();
    }

    public void RemoveSession(string userId)
    {
        _sessions.TryRemove(userId, out _);
    }

    public bool IsRunning(string userId) => _sessions.ContainsKey(userId);
}
