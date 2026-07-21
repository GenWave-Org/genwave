namespace GenWave.Tts;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Live settings subscriber for <c>Tts:Corrections</c> (SPEC F68.5, STORY-185 AC1). Subscribes to
/// <see cref="IOptionsMonitor{TOptions}.OnChange"/> once at construction and rebuilds an immutable
/// <see cref="SpeechCorrectionSet"/> snapshot on every change, so a rule saved through
/// <c>PUT /api/settings</c> reaches the very next render with no api restart.
///
/// <see cref="Current"/> is a plain field read (backed by <see langword="volatile"/>) — every
/// render reads it fresh; nothing here ever hands out a stale snapshot captured at some earlier
/// point in the process lifetime.
///
/// Malformed JSON degrades to <see cref="SpeechCorrectionSet.Empty"/> with one WARN rather than
/// throwing — a typo in the stored corrections must never break every subsequent render (the same
/// degrade-not-throw discipline <see cref="LlmCopyWriter"/> applies to a bad LLM response).
/// Registered as a singleton (<see cref="TtsServiceCollectionExtensions.AddGenWaveTts"/>) so the
/// one subscription lives for the process lifetime.
/// </summary>
public sealed class SpeechCorrectionProvider : IDisposable
{
    // Sentinel for "no rules configured" — a stable literal rather than SHA256-of-empty-string, so
    // the no-corrections case never depends on the hash algorithm's own behavior on empty input and
    // reads unambiguously in logs/cache-file names. Distinct from
    // ActivePersonaCorrectionsCache's own "no-card-corrections" sentinel (the card side of the
    // F71.7 merge) so the two independent "no rules" cases can never collide with each other either.
    const string EmptyContentHash = "no-corrections";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    readonly ILogger<SpeechCorrectionProvider> logger;
    readonly IDisposable? subscription;

    // Set and ContentHash are always derived from the SAME Build call and swapped together via one
    // reference assignment — never two independent volatile fields — so a reader can never observe
    // Current from one rebuild paired with ContentHash from another.
    volatile Snapshot snapshot;
    long version;

    public SpeechCorrectionProvider(
        IOptionsMonitor<TtsCorrectionsOptions> optionsMonitor,
        ILogger<SpeechCorrectionProvider> logger)
    {
        this.logger = logger;
        snapshot = Build(optionsMonitor.CurrentValue, logger);
        subscription = optionsMonitor.OnChange(updated =>
        {
            snapshot = Build(updated, logger);
            Interlocked.Increment(ref version);
        });
    }

    /// <summary>The current immutable snapshot of operator corrections.</summary>
    public SpeechCorrectionSet Current => snapshot.Set;

    /// <summary>
    /// The station-over-card merge seam (SPEC F71.7, STORY-193): compiles <paramref
    /// name="cardCorrections"/> the same way <see cref="Current"/>'s own station rules are compiled
    /// (<see cref="SpeechCorrectionSet.Create"/>) and merges them beneath <paramref
    /// name="stationSet"/> via <see cref="SpeechCorrectionSet.Merge"/> — station wins on an
    /// identical <see cref="SpeechCorrection.From"/> (case-insensitive). A free function rather
    /// than an instance method: it needs no state of its own beyond the two sets handed to it, so
    /// callers (<see cref="NormalizingTtsSynthesizer"/>) can build the merged snapshot at their own
    /// render-time cadence without this provider knowing anything about the card side at all.
    /// </summary>
    public static SpeechCorrectionSet BuildMerged(
        SpeechCorrectionSet stationSet, IReadOnlyList<SpeechCorrection> cardCorrections) =>
        SpeechCorrectionSet.Merge(stationSet, SpeechCorrectionSet.Create(cardCorrections));

    /// <summary>
    /// Deterministic content fingerprint of the current correction rules (SPEC F68.5), via <see
    /// cref="CorrectionsFingerprint.Compute"/> over the canonical, ordered (From, To) pairs <see
    /// cref="Current"/> actually compiled; a rule set with no rules at all yields the stable
    /// constant <c>"no-corrections"</c> rather than a hash of empty input. Same rules always fold to
    /// the same fingerprint, in this process or the next one — unlike a process-local counter, it
    /// does not reset to a colliding starting value across a container redeploy. This is one of the
    /// two terms <see cref="TtsSegmentSource"/> folds into its cache key (the other is <see
    /// cref="ActivePersonaCorrectionsCache.ContentHash"/>, the card side of the F71.7 merge): same
    /// rules → same key across restarts, changed rules → a new key on the very next render.
    /// </summary>
    public string ContentHash => snapshot.ContentHash;

    /// <summary>
    /// Observability-only generation counter — how many times <see cref="Current"/> has been
    /// rebuilt from an <see cref="IOptionsMonitor{TOptions}.OnChange"/> notification since this
    /// process started. Starts at 0 (the construction-time snapshot) and is NOT a cache-key term:
    /// it resets to 0 on every process restart, so keying a cache on it collides "same rules, fresh
    /// process" with "different rules, mid-life rebuild" the moment two processes' counters happen
    /// to line up — exactly the bug <see cref="ContentHash"/> exists to avoid. Kept purely so a
    /// future admin surface can report "corrections reloaded N times this process".
    /// </summary>
    public long Version => Interlocked.Read(ref version);

    static Snapshot Build(TtsCorrectionsOptions options, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.Corrections))
            return new Snapshot(SpeechCorrectionSet.Empty, EmptyContentHash);

        try
        {
            var rules = JsonSerializer.Deserialize<List<SpeechCorrection>>(options.Corrections, JsonOptions);
            if (rules is null)
                return new Snapshot(SpeechCorrectionSet.Empty, EmptyContentHash);

            var set = SpeechCorrectionSet.Create(rules);
            return new Snapshot(set, ComputeContentHash(set));
        }
        catch (Exception ex)
        {
            // Deliberately broad (not just JsonException): Tts:Corrections is operator-authored data,
            // never trusted deployment topology, so ANY deserialization surprise — malformed JSON, or
            // a null array element STJ happily produces from e.g. "[null]" — must degrade to no
            // corrections with one WARN rather than escape the constructor and take the api down.
            logger.LogWarning(
                ex, "Tts:Corrections could not be parsed; no operator corrections applied until it is fixed");
            return new Snapshot(SpeechCorrectionSet.Empty, EmptyContentHash);
        }
    }

    static string ComputeContentHash(SpeechCorrectionSet set) =>
        CorrectionsFingerprint.Compute(set.RulePairs, EmptyContentHash);

    public void Dispose() => subscription?.Dispose();

    sealed record Snapshot(SpeechCorrectionSet Set, string ContentHash);
}
