namespace GenWave.Orchestration;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// The default <see cref="IPatterDurationEstimator"/> (gh-#253) — in-memory only, no I/O, no
/// migrations. Three honest tiers, best first:
///
/// <list type="number">
/// <item><b>Exact</b> — <see cref="SegmentKind.StationId"/> only: its copy is deterministic per
/// (voice, on-air show) — always templated (<c>LlmCopyWriter.IsLlmAuthored</c> reports false for
/// it), so the TTS cache serves the SAME rendered file on every airing of that (voice, show) pair and
/// the last measured duration for it IS that pair's next airing duration. SPEC F117.2 (STORY-309,
/// PLAN T250 review finding F1) widened the memo key from voice alone to (voice, show-name-or-null):
/// the templated show line ("You're listening to {show} on {station}.") is a DIFFERENT rendered clip
/// than the plain ident ("You're listening to {station}.") for the SAME voice, so a voice-only key
/// would let one show's measured duration silently stand in for another show's — or the plain
/// ident's — airing. <see langword="null"/> show-name is its own bucket (the F110.2-original,
/// showless ident). A corrections/settings edit that re-keys the TTS cache simply re-measures on the
/// next render and the very next observation replaces that (voice, show) memo. No other kind
/// qualifies today (LeadIn/BackAnnounce vary per track, TimeDate per clock read, SignOff/SignOn are
/// LLM-authored blurbs) — a future render-ahead producer that holds a rendered segment in hand needs
/// no tier here at all, it already has the real <c>DurationMs</c>.</item>
/// <item><b>Historical</b> — a per-(persona × kind) rolling average over the last
/// <see cref="HistoryWindow"/> MEASURED durations (SPEC F66.1's cue-derived stamp, observed back in
/// via <see cref="ObserveRendered"/>), reported once <see cref="MinHistoricalSamples"/> samples
/// exist. Deliberately NOT booth-log-backed: <c>station.booth_log</c>'s patter rows carry no
/// duration column (the <c>SegmentGenerated</c> event never carried one), so a 14-day DB-backed
/// average would need BOTH a schema migration and an event change — this in-process ring warms
/// within the first few units after boot instead, and the cold tier below covers the gap honestly.</item>
/// <item><b>Heuristic</b> — chars-per-second over the kind's expected copy length, with the live
/// <c>Llm:MaxCopyChars</c> (via <paramref name="copyBounds"/>, read fresh per call) bounding the
/// LLM-authored kinds' worst case. Fewer than <see cref="MinHistoricalSamples"/> real samples still
/// beat the chars guess (the average is used) but are REPORTED at this tier — one data point is not
/// yet a trend, and the consumer's tolerance should stay wide.</item>
/// </list>
///
/// Thread-safe via one lock — every operation is a few dictionary reads over tiny state, uncontended
/// at cadence scale (a handful of calls per planning pass).
/// </summary>
/// <param name="copyBounds">
/// The live <c>Llm:MaxCopyChars</c> seam; <see langword="null"/> (an unwired host or a bare unit
/// test) falls back to <see cref="DefaultMaxCopyChars"/> — <c>LlmOptions.MaxCopyChars</c>'s own
/// shipped default, duplicated here because this project cannot reference <c>GenWave.Tts</c>.
/// </param>
public sealed class RollingPatterDurationEstimator(ICopyBoundsProvider? copyBounds = null)
    : IPatterDurationEstimator
{
    /// <summary>Mirror of <c>LlmOptions.MaxCopyChars</c>'s default (SPEC F34.5) — see the ctor param remarks.</summary>
    const int DefaultMaxCopyChars = 450;

    /// <summary>Samples kept per (persona × kind) key — enough to smooth persona/LLM variance without
    /// letting a week-old outlier linger forever.</summary>
    const int HistoryWindow = 20;

    /// <summary>Below this many samples the average is still used but reported at
    /// <see cref="PatterEstimateConfidence.Heuristic"/> — one data point is not a trend.</summary>
    const int MinHistoricalSamples = 3;

    /// <summary>
    /// Speaking rate for the cold tier: ~150 wpm at ~6 chars/word ≈ 15 chars/s — a deliberate
    /// single default rather than a per-voice table (no per-voice rate data exists anywhere yet;
    /// the interface's <c>voice</c> parameter leaves room for one once it does).
    /// </summary>
    const double CharsPerSecond = 15.0;

    /// <summary>Expected copy length for the LLM-authored kinds' typical "one or two sentences"
    /// (the house prompt scaffold, SPEC F34.3) — capped by the live MaxCopyChars at estimate time.</summary>
    const int TypicalLlmCopyChars = 170;

    // Templated kinds' expected copy lengths — judged from PatterTemplateRenderer's own arms
    // ("You're listening to {name}." / "It's {h:mm tt} here on {name}.").
    const int TypicalStationIdChars = 40;
    const int TypicalTimeDateChars = 55;

    /// <summary>Floor under every heuristic answer — even a one-word clip takes a couple of seconds
    /// of real air (breath, pacing, the synthesizer's own lead-out).</summary>
    static readonly TimeSpan HeuristicFloor = TimeSpan.FromSeconds(2);

    readonly object gate = new();
    readonly Dictionary<(SegmentKind Kind, string Persona), Queue<double>> history = new();

    // SPEC F117.2 (STORY-309, PLAN T250 review finding F1) — keyed on (voice, show-name-or-null),
    // never voice alone: see the class remarks' Exact-tier paragraph for why a voice-only key would
    // let one show's (or the plain ident's) measured duration silently stand in for another's. Default
    // tuple/string equality is ordinal (String's own IEquatable&lt;string&gt; implementation), the
    // same comparison the old voice-only dictionary's explicit StringComparer.Ordinal gave.
    readonly Dictionary<(string Voice, string? ShowName), double> exactStationIdMs = new();

    /// <inheritdoc/>
    public PatterDurationEstimate Estimate(SegmentKind kind, string? personaName, string voice) =>
        Estimate(kind, personaName, voice, showName: null);

    /// <inheritdoc/>
    public PatterDurationEstimate Estimate(SegmentKind kind, string? personaName, string voice, string? showName)
    {
        lock (gate)
        {
            // Tier 1 — exact: the cache-stable StationId clip replays verbatim (see class remarks).
            if (kind == SegmentKind.StationId && exactStationIdMs.TryGetValue((voice, showName), out var exactMs))
                return new PatterDurationEstimate(TimeSpan.FromMilliseconds(exactMs), PatterEstimateConfidence.Exact);

            // Tier 2 — historical rolling average, per persona × kind.
            if (history.TryGetValue((kind, personaName ?? ""), out var samples) && samples.Count > 0)
            {
                var average = TimeSpan.FromMilliseconds(samples.Average());
                return new PatterDurationEstimate(
                    average,
                    samples.Count >= MinHistoricalSamples
                        ? PatterEstimateConfidence.Historical
                        : PatterEstimateConfidence.Heuristic);
            }
        }

        // Tier 3 — cold chars-per-second heuristic (outside the lock: copyBounds is a live read of
        // its own and this path touches no shared state).
        return new PatterDurationEstimate(HeuristicFor(kind), PatterEstimateConfidence.Heuristic);
    }

    /// <inheritdoc/>
    public void ObserveRendered(SegmentKind kind, string? personaName, string voice, TimeSpan measured) =>
        ObserveRendered(kind, personaName, voice, measured, showName: null);

    /// <inheritdoc/>
    public void ObserveRendered(SegmentKind kind, string? personaName, string voice, TimeSpan measured, string? showName)
    {
        if (measured <= TimeSpan.Zero) return; // measured-never-fabricated (F66.1) — a non-positive value is neither

        lock (gate)
        {
            if (kind == SegmentKind.StationId)
                exactStationIdMs[(voice, showName)] = measured.TotalMilliseconds;

            var key = (kind, personaName ?? "");
            if (!history.TryGetValue(key, out var samples))
            {
                samples = new Queue<double>(HistoryWindow);
                history[key] = samples;
            }

            samples.Enqueue(measured.TotalMilliseconds);
            while (samples.Count > HistoryWindow) samples.Dequeue();
        }
    }

    TimeSpan HeuristicFor(SegmentKind kind)
    {
        var maxCopyChars = copyBounds?.MaxCopyChars ?? DefaultMaxCopyChars;
        var expectedChars = kind switch
        {
            SegmentKind.StationId => TypicalStationIdChars,
            SegmentKind.TimeDate => TypicalTimeDateChars,
            // The LLM-authored kinds (LeadIn/BackAnnounce/SignOff/SignOn/ContextSegment as of T224 —
            // LlmCopyWriter.IsLlmAuthored): typical two-sentence copy, with the live MaxCopyChars
            // bounding the worst case (gh-#253). Deliberately NOT exhaustive by name — a future
            // LLM-eligible kind lands here automatically via this same fallback arm, never a throw.
            _ => Math.Min(TypicalLlmCopyChars, maxCopyChars),
        };

        var estimate = TimeSpan.FromSeconds(expectedChars / CharsPerSecond);
        return estimate < HeuristicFloor ? HeuristicFloor : estimate;
    }
}
