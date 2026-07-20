namespace GenWave.Tts;

using System.Collections.Concurrent;

/// <summary>
/// Process-lifetime, per-rule fired counters for operator pronunciation corrections (SPEC F68.7,
/// STORY-186 AC3) — how many times each <see cref="SpeechCorrection.From"/> (case-insensitive) has
/// actually changed booth-bound text since this process started. Incremented by
/// <see cref="NormalizingTtsSynthesizer"/> immediately after a REAL render (never a preview) applies
/// corrections; read by <c>GET /api/tts/corrections-stats</c> so an operator can confirm a saved
/// rule is actually firing on-air. In-memory only, mirroring <see cref="LlmCopyStatusHolder"/>'s own
/// no-persistence contract — restarting the api resets every count to zero. Registered as a
/// singleton (<see cref="TtsServiceCollectionExtensions.AddGenWaveTts"/>) so the one counter set
/// lives for the process lifetime.
/// </summary>
public sealed class CorrectionsFiredStats
{
    readonly ConcurrentDictionary<string, long> fired = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Increments the fired count for <paramref name="from"/> by one.</summary>
    public void RecordFired(string from) => fired.AddOrUpdate(from, 1, static (_, count) => count + 1);

    /// <summary>
    /// A snapshot of every rule that has fired at least once since process start, in no particular
    /// order. A rule that has never fired is simply absent — never a zero-count row.
    /// </summary>
    public IReadOnlyList<(string From, long Fired)> Snapshot() =>
        fired.Select(entry => (entry.Key, entry.Value)).ToList();
}
