namespace FinvizMcpServer.Finviz;

/// <summary>
/// Client-side limiter for the Finviz Elite request budget: 10 requests per rolling 5-second window,
/// shared across every Export/API endpoint on the account. Registered as a singleton so all tool calls
/// draw from the same budget and callers wait instead of tripping HTTP 429.
/// </summary>
public sealed class FinvizRateLimiter
{
    public const int MaxRequests = 10;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    private readonly Queue<long> _sent = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _time = TimeProvider.System;

    /// <summary>Waits until a request slot is free in the sliding window, then claims it.</summary>
    public async Task WaitAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            while (true)
            {
                var now = _time.GetTimestamp();
                while (_sent.Count > 0 && _time.GetElapsedTime(_sent.Peek(), now) >= Window)
                {
                    _sent.Dequeue();
                }

                if (_sent.Count < MaxRequests)
                {
                    _sent.Enqueue(now);
                    return;
                }

                await Task.Delay(Window - _time.GetElapsedTime(_sent.Peek(), now), _time, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
