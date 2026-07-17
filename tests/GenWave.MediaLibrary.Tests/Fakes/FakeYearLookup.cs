using GenWave.Core.Abstractions;
using GenWave.MediaLibrary.YearLookup;

namespace GenWave.MediaLibrary.Tests.Fakes;

/// <summary>
/// Returns a configurable, queued sequence of nullable years without touching HTTP or the network
/// (SPEC F48.7 — unit tests must never reach it). Implements <see cref="IYearLookupDiagnostics"/> so
/// specs can also exercise <c>EnrichmentService</c>'s WARN-once-per-tick aggregation (F48.5) without
/// a real <see cref="GenWave.MediaLibrary.YearLookup.MusicBrainzYearLookup"/>.
///
/// Every call is recorded (arguments, start timestamp, in-flight depth) so pacing/concurrency specs
/// (<c>ScenarioAttemptsArePacedAndSingleFile</c>) can assert on the shape of a whole batch, not just
/// its final result. A short artificial delay inside the call makes a would-be concurrency bug
/// (two calls actually overlapping) observable as <see cref="MaxObservedConcurrency"/> &gt; 1 rather
/// than racing past unnoticed.
/// </summary>
sealed class FakeYearLookup : IYearLookup, IYearLookupDiagnostics
{
    readonly object gate = new();
    readonly Queue<(int? Year, bool Failed)> queue = new();
    (int? Year, bool Failed) fallback = (null, false);
    int inFlight;

    public int Calls { get; private set; }
    public int MaxObservedConcurrency { get; private set; }
    public List<DateTime> CallStarts { get; } = [];
    public List<(string Artist, string Title, string? Album)> CallArgs { get; } = [];

    public bool LastCallFailed { get; private set; }

    /// <summary>Queues one result for the next call; calls beyond the queue fall back to <see cref="SetFallback"/>.</summary>
    public void Enqueue(int? year, bool failed = false) => queue.Enqueue((year, failed));

    /// <summary>The result every call uses once the queue is drained (default: null, not failed).</summary>
    public void SetFallback(int? year, bool failed = false) => fallback = (year, failed);

    public async Task<int?> TryLookupAsync(string artist, string title, string? album, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        int concurrency;
        lock (gate)
        {
            Calls++;
            CallStarts.Add(DateTime.UtcNow);
            CallArgs.Add((artist, title, album));
            concurrency = ++inFlight;
            if (concurrency > MaxObservedConcurrency)
                MaxObservedConcurrency = concurrency;
        }

        try
        {
            // A small artificial delay so a genuine pacing bug (the service accidentally starting a
            // second call before the first returns) is observable rather than racing to completion
            // faster than two overlapping calls could ever be told apart.
            await Task.Delay(10, ct);

            (int? Year, bool Failed) result;
            lock (gate)
                result = queue.Count > 0 ? queue.Dequeue() : fallback;

            LastCallFailed = result.Failed;
            return result.Year;
        }
        finally
        {
            lock (gate)
                inFlight--;
        }
    }
}
