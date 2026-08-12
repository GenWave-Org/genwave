namespace GenWave.Tts;

using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;

/// <summary>
/// The single hand-off point from booth-bound copy to the TTS renderer (SPEC F68.1). Every caller
/// of <see cref="ITtsSynthesizer"/> — <see cref="TtsSegmentSource"/> (patter: LLM copy, template
/// copy), <see cref="SafeSegmentAuthor"/> (authored/safe-loop segments), and the admin preview
/// endpoint — resolves this decorator, so <see cref="SpeechText.Normalize"/> runs exactly once,
/// right here, immediately before the real synthesis call. No caller performs its own pre-TTS
/// cleanup; this IS "the hand-off to the TTS renderer" F68.1 requires, not a caller-side concern.
///
/// Decorates the concrete synthesizer (mirrors <see cref="CachedVoiceLister"/>'s decorator shape
/// one seam over on <see cref="ITtsVoiceLister"/>) rather than being folded into
/// <see cref="KokoroTtsSynthesizer"/> itself, so the HTTP client stays a pure Kokoro adapter and
/// this stays a pure text-hand-off concern — two reasons to change, two classes.
///
/// Also implements <see cref="ISpeechNormalizationPreview"/> (SPEC F68.6, STORY-186 AC2): the admin
/// preview endpoint resolves this SAME registered instance (see <see cref="LlmCopyWriter"/>'s
/// analogous two-seam registration) so a preview can never drift from what a real render produces,
/// with no TTS render and no observability side effects.
///
/// Fired-rule observability (SPEC F68.7 as amended by F97.5/F100.1, STORY-186 AC3): every real
/// render through <see cref="SynthesizeAsync(TtsRenderContext, CancellationToken)"/> logs one
/// Information line and increments <see cref="CorrectionsFiredStats"/> per rule that actually
/// changed the text, read back by <c>GET /api/tts/corrections-stats</c>. F68.7 originally specified
/// debug; debug never reaches the fleet log store at all, so "is my rule working?" was unanswerable
/// in the field from the moment it shipped — F97.5 amends every rule-hit fact in this family to
/// Information on that same ground (PLAN T142).
///
/// <b>"Every real render" DELIBERATELY includes the admin AUDIO preview</b>
/// (<c>POST /api/tts/preview</c>, <c>TtsPreviewController</c>): that endpoint calls the plain
/// <see cref="SynthesizeAsync(string, string, CancellationToken)"/> overload, which wraps its
/// arguments into a <see cref="TtsRenderContext"/> and relays through to THIS method — "the same
/// production hand-off every render path shares" (STORY-186 AC3's own wording) — so a rule that
/// fires during an admin preview counts and logs at Information exactly like one that fires on
/// air. Only <see cref="Preview"/> below (the TEXT-only <see cref="ISpeechNormalizationPreview"/>
/// seam, <c>POST /api/tts/normalize-preview</c> — no synthesis call at all) is excluded from this;
/// see its own remarks. Contrast <see cref="PronunciationRuleHitReporter"/>: pronunciation-rule
/// hits ARE excluded from the same admin audio preview (SPEC F97.5, STORY-253 AC6) — the two
/// families deliberately part ways on this one point, not a drift between them. F97.5's own
/// preview carve-out is scoped, by its text, to pronunciation rules; it never restates as covering
/// corrections, and STORY-186 AC3 (which predates F97.5) already pinned the opposite for
/// corrections.
///
/// <see cref="SpeechCorrectionSet"/> itself stays pure (it only reports which rules fired via an out
/// parameter); this decorator is where that report becomes a log line and a counter.
///
/// <see cref="personaCorrections"/> supplies the card half of the F97.4 merge (STORY-193, amending
/// F71.7): every real render refreshes it (bounded by its own staleness window — see its class
/// remarks) before <see cref="SpeechCorrectionProvider.BuildMerged"/> builds the snapshot <see
/// cref="RunNormalize"/> actually matches against — the one place the merged set is built. Card
/// rules sort ahead of station rules there; for exactly what that does and does NOT guarantee, see
/// <see cref="PersonaOverStationMerge"/>, which states the invariant once so it cannot drift.
/// </summary>
public sealed class NormalizingTtsSynthesizer(
    ITtsSynthesizer inner,
    SpeechCorrectionProvider corrections,
    ActivePersonaCorrectionsCache personaCorrections,
    CorrectionsFiredStats firedStats,
    ILogger<NormalizingTtsSynthesizer> logger) : ITtsSynthesizer, ISpeechNormalizationPreview
{
    public Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct) =>
        SynthesizeAsync(new TtsRenderContext(text, voice, Kind: null), ct);

    /// <summary>
    /// Kind-aware overload (SPEC F70.3, STORY-191): normalizes exactly as the plain two-arg overload
    /// always has, but relays <see cref="TtsRenderContext.Kind"/> unchanged to <paramref
    /// name="inner"/> (typically <see cref="FallbackTtsSynthesizer"/>) so its per-kind engine map can
    /// see which speech kind is rendering. This decorator never inspects <c>Kind</c> itself —
    /// normalization and correction firing are kind-agnostic — it only relays it downstream.
    /// </summary>
    public async Task<string> SynthesizeAsync(TtsRenderContext context, CancellationToken ct)
    {
        // Real render only (SPEC F71.7): refreshes personaCorrections.Current when its own TTL has
        // elapsed, then builds the merged snapshot fresh for THIS render — never called from
        // Preview below, which reads whatever the cache last held with no refresh (see
        // ActivePersonaCorrectionsCache's own remarks on the two paths' different staleness bounds).
        await personaCorrections.RefreshIfStaleAsync(ct);
        var snapshot = SpeechCorrectionProvider.BuildMerged(corrections.Current, personaCorrections.Current);
        var normalized = RunNormalize(context.Text, snapshot);
        ReportFiredCorrections(context.Text, context.Voice, snapshot);
        return await inner.SynthesizeAsync(context with { Text = normalized }, ct);
    }

    /// <inheritdoc/>
    public string Preview(string text) =>
        RunNormalize(text, SpeechCorrectionProvider.BuildMerged(corrections.Current, personaCorrections.Current));

    /// <summary>
    /// The one call to <see cref="SpeechText.Normalize"/> in this codebase (SPEC F68.1) — both
    /// <see cref="SynthesizeAsync"/> and <see cref="Preview"/> funnel through here so a preview can
    /// never drift from what a real render produces.
    /// </summary>
    static string RunNormalize(string text, SpeechCorrectionSet snapshot) => SpeechText.Normalize(text, snapshot);

    /// <summary>
    /// Determines which rules fired for THIS render — via <see cref="SpeechText.PrepareForCorrections"/>
    /// and <see cref="SpeechCorrectionSet.Apply"/>'s out parameter, the same pre-corrections text
    /// <see cref="RunNormalize"/> itself matches against — and logs/counts each one. Never called
    /// from <see cref="Preview"/> (the TEXT-only <c>POST /api/tts/normalize-preview</c> seam, which
    /// performs no synthesis at all): THAT preview is not a broadcast, so it must not pollute the
    /// operator-facing fired counters or the Information log with trial runs. The admin AUDIO
    /// preview (<c>POST /api/tts/preview</c>) is different — it reaches this method through the
    /// plain <see cref="SynthesizeAsync(string, string, CancellationToken)"/> overload exactly like
    /// any other real render (STORY-186 AC3, deliberate; see the class remarks for the full
    /// corrections-vs-pronunciation-rules asymmetry).
    /// </summary>
    void ReportFiredCorrections(string text, string voice, SpeechCorrectionSet snapshot)
    {
        var prepared = SpeechText.PrepareForCorrections(text);
        snapshot.Apply(prepared, out var firedFroms);

        foreach (var from in firedFroms)
        {
            firedStats.RecordFired(from);
            // Operator-authored rule text and voice id are newline-stripped so they can't forge
            // additional log entries (CodeQL cs/log-forging) — LogSanitize.Strip, converging onto
            // PronunciationRuleHitReporter's own idiom (PLAN T142 review) rather than this method's
            // prior ReplaceLineEndings(" "): one sanitizer for the whole rule-hit family. Information,
            // not debug (SPEC F68.7 as amended by F97.5/F100.1, PLAN T142) — debug never reaches the
            // fleet log store, so a correction hit was unobservable in the field before this.
            logger.LogInformation(
                "TTS correction fired: from={CorrectionFrom} voice={Voice}",
                LogSanitize.Strip(from), LogSanitize.Strip(voice));
        }
    }
}
