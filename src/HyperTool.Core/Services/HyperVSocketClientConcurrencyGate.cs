using System.Diagnostics;
using System.Threading;

namespace HyperTool.Services;

public static class HyperVSocketClientConcurrencyGate
{
    private const int MaxConcurrentOutboundSockets = 4;
    private static readonly SemaphoreSlim Gate = new(MaxConcurrentOutboundSockets, MaxConcurrentOutboundSockets);
    private static int _inflight;
    private static int _peakInflight;
    private static int _waiters;
    private static long _totalAcquireCount;
    private static long _totalAcquireWaitMs;
    private static long _slowAcquireCount;

    public readonly struct Snapshot
    {
        public int MaxConcurrentOutboundSockets { get; init; }
        public int AvailableSlots { get; init; }
        public int Inflight { get; init; }
        public int PeakInflight { get; init; }
        public int Waiters { get; init; }
        public long TotalAcquireCount { get; init; }
        public long TotalAcquireWaitMs { get; init; }
        public long SlowAcquireCount { get; init; }
        public double AverageAcquireWaitMs { get; init; }
    }

    public static async ValueTask<Lease> AcquireAsync(CancellationToken cancellationToken)
    {
        var waitStart = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _waiters);
        await Gate.WaitAsync(cancellationToken);
        Interlocked.Decrement(ref _waiters);

        var waitedMs = (long)Math.Round((Stopwatch.GetTimestamp() - waitStart) * 1000d / Stopwatch.Frequency);
        Interlocked.Increment(ref _totalAcquireCount);
        Interlocked.Add(ref _totalAcquireWaitMs, Math.Max(0L, waitedMs));
        if (waitedMs >= 50)
        {
            Interlocked.Increment(ref _slowAcquireCount);
        }

        var inflight = Interlocked.Increment(ref _inflight);
        while (true)
        {
            var currentPeak = Volatile.Read(ref _peakInflight);
            if (inflight <= currentPeak)
            {
                break;
            }

            if (Interlocked.CompareExchange(ref _peakInflight, inflight, currentPeak) == currentPeak)
            {
                break;
            }
        }

        return new Lease();
    }

    public static Snapshot GetSnapshot()
    {
        var totalAcquireCount = Interlocked.Read(ref _totalAcquireCount);
        var totalAcquireWaitMs = Interlocked.Read(ref _totalAcquireWaitMs);

        return new Snapshot
        {
            MaxConcurrentOutboundSockets = MaxConcurrentOutboundSockets,
            AvailableSlots = Gate.CurrentCount,
            Inflight = Volatile.Read(ref _inflight),
            PeakInflight = Volatile.Read(ref _peakInflight),
            Waiters = Volatile.Read(ref _waiters),
            TotalAcquireCount = totalAcquireCount,
            TotalAcquireWaitMs = totalAcquireWaitMs,
            SlowAcquireCount = Interlocked.Read(ref _slowAcquireCount),
            AverageAcquireWaitMs = totalAcquireCount <= 0
                ? 0
                : Math.Round(totalAcquireWaitMs / (double)totalAcquireCount, 2)
        };
    }

    public readonly struct Lease : IDisposable
    {
        public void Dispose()
        {
            Interlocked.Decrement(ref _inflight);
            Gate.Release();
        }
    }
}