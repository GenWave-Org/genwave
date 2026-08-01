namespace GenWave.Tts;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Live settings subscriber for <c>Tts:Pronunciations</c> (SPEC F97.3, STORY-253) — the station half
/// of the pronunciation-rule merge, mirroring <see cref="SpeechCorrectionProvider"/>'s shape exactly.
/// Subscribes to <see cref="IOptionsMonitor{TOptions}.OnChange"/> once at construction and rebuilds
/// an immutable <see cref="PronunciationRuleSet"/> snapshot on every change, so a rule saved through
/// <c>PUT /api/settings</c> reaches the very next render with no api restart.
///
/// <see cref="Current"/> is a plain field read (backed by <see langword="volatile"/>) — every render
/// reads it fresh. Malformed JSON degrades to <see cref="PronunciationRuleSet.Empty"/> with one WARN
/// rather than throwing — a typo in the stored rules must never break every subsequent render.
/// Registered as a singleton (<see cref="TtsServiceCollectionExtensions.AddGenWaveTts"/>) so the one
/// subscription lives for the process lifetime.
/// </summary>
public sealed class PronunciationRuleProvider : IDisposable
{
    // Sentinel for "no rules configured" — distinct from ActivePersonaPronunciationRulesCache's own
    // "no-card-pronunciations" sentinel so the two independent "no rules" cases can never collide.
    const string EmptyContentHash = "no-pronunciations";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    readonly ILogger<PronunciationRuleProvider> logger;
    readonly IDisposable? subscription;

    // Set and ContentHash are always derived from the SAME Build call and swapped together via one
    // reference assignment — never two independent volatile fields — so a reader can never observe
    // Current from one rebuild paired with ContentHash from another.
    volatile Snapshot snapshot;

    public PronunciationRuleProvider(
        IOptionsMonitor<TtsPronunciationsOptions> optionsMonitor,
        ILogger<PronunciationRuleProvider> logger)
    {
        this.logger = logger;
        snapshot = Build(optionsMonitor.CurrentValue, logger);
        subscription = optionsMonitor.OnChange(updated => snapshot = Build(updated, logger));
    }

    /// <summary>The current immutable snapshot of station pronunciation rules.</summary>
    public PronunciationRuleSet Current => snapshot.Set;

    /// <summary>
    /// Deterministic content fingerprint of the current rule set (SPEC F97.3), via
    /// <see cref="PronunciationRuleFingerprint.Compute"/> over the canonical, ordered rules
    /// <see cref="Current"/> actually compiled. One of the two terms <see cref="TtsSegmentSource"/>
    /// folds into its cache key (the other is <see cref="ActivePersonaPronunciationRulesCache.ContentHash"/>,
    /// the card side of the merge): same rules → same key across restarts, changed rules → a new
    /// key on the very next render.
    /// </summary>
    public string ContentHash => snapshot.ContentHash;

    /// <summary>
    /// The persona-over-station merge seam (SPEC F97.3, F97.4): merges <paramref name="cardRules"/>
    /// (compiled the same way <see cref="Current"/>'s own station rules are) over
    /// <paramref name="stationSet"/> via <see cref="PronunciationRuleSet.Merge"/> — every card rule
    /// ordered ahead of every station rule. The precise guarantee is stated once, in
    /// <see cref="PersonaOverStationMerge"/>; not restated here. A free function, mirroring
    /// <see cref="SpeechCorrectionProvider.BuildMerged"/>: it needs no state of its own beyond the
    /// two sets handed to it, so callers (<see cref="TtsSegmentSource"/>) build the merged snapshot
    /// at their own render-time cadence without this provider knowing anything about the card side.
    /// </summary>
    public static PronunciationRuleSet BuildMerged(
        PronunciationRuleSet stationSet, IReadOnlyList<PronunciationRule> cardRules) =>
        PronunciationRuleSet.Merge(stationSet, PronunciationRuleSet.Create(cardRules));

    static Snapshot Build(TtsPronunciationsOptions options, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.Pronunciations))
            return new Snapshot(PronunciationRuleSet.Empty, EmptyContentHash);

        try
        {
            var rules = JsonSerializer.Deserialize<List<PronunciationRule>>(options.Pronunciations, JsonOptions);
            if (rules is null)
                return new Snapshot(PronunciationRuleSet.Empty, EmptyContentHash);

            var set = PronunciationRuleSet.Create(rules);
            var compiledCount = set.Rules.Count();
            if (compiledCount < rules.Count)
            {
                // SPEC F97.5 review finding: SettingValidator only guards Tts:Pronunciations' JSON
                // shape (Story253_PronunciationsSettingShape), never whether a rule actually compiles
                // — a rule that PronunciationRuleSet.Create dropped (blank pattern/word/ipa, an ipa
                // carrying ')'/'['/']', a word not found inside its own pattern, or a null array
                // element) would otherwise never fire and never be logged anywhere. T142's rule-HIT
                // counters can never surface this either: a dropped rule never reaches Match, so it
                // never hits. One WARN here, at construction/every rebuild, is the earliest and only
                // place every station rule passes through that can compare "declared" against
                // "compiled" and say so.
                logger.LogWarning(
                    "Tts:Pronunciations declared {DeclaredCount} rule(s) but only {CompiledCount} compiled — " +
                    "the rest were dropped (blank pattern/word/ipa, an ipa containing ')'/'['/']', or a word " +
                    "not found inside its own pattern) and will never fire",
                    rules.Count, compiledCount);
            }
            return new Snapshot(set, ComputeContentHash(set));
        }
        catch (Exception ex)
        {
            // Deliberately broad (not just JsonException): Tts:Pronunciations is operator-authored
            // data, never trusted deployment topology, so ANY deserialization surprise — malformed
            // JSON, or a null array element STJ happily produces from e.g. "[null]" — must degrade to
            // no rules with one WARN rather than escape the constructor and take the api down.
            logger.LogWarning(
                ex, "Tts:Pronunciations could not be parsed; no station pronunciation rules applied until it is fixed");
            return new Snapshot(PronunciationRuleSet.Empty, EmptyContentHash);
        }
    }

    static string ComputeContentHash(PronunciationRuleSet set) =>
        PronunciationRuleFingerprint.Compute(set.Rules, EmptyContentHash);

    public void Dispose() => subscription?.Dispose();

    sealed record Snapshot(PronunciationRuleSet Set, string ContentHash);
}
