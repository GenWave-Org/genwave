namespace GenWave.Tts;

using Microsoft.Extensions.Logging;
using GenWave.Core.Domain;
using GenWave.Core.Logging;

/// <summary>
/// Logs and counts pronunciation-rule hits (SPEC F97.5, F100.1, STORY-253 AC4) — the one seam both
/// Kokoro-kind request builders (<see cref="KokoroTtsSynthesizer"/>, the primary; and
/// <see cref="KokoroFallbackRenderer"/>, a fallback hop) call, each only AFTER its own render has
/// fully landed: the engine returned success AND the audio was written to disk (review finding,
/// PLAN T142) — never merely after <see cref="KokoroSpeechMarkup"/>'s out-matches overload finds a
/// match. "Fired" means "aired": reporting any earlier double-counts the one line that actually
/// airs when a failing primary is retried by a succeeding fallback hop, and counts a hit for a
/// render that never airs at all when the engine call or the subsequent write fails. Factored into
/// its own class rather than duplicated inside each renderer — mirrors
/// <see cref="NormalizingTtsSynthesizer.ReportFiredCorrections"/>'s "detection stays pure, a
/// stateful collaborator turns the report into a log line and a counter" shape one seam over —
/// because pronunciation markup is Kokoro-only and composed INSIDE two independent request
/// builders, not at the one <see cref="NormalizingTtsSynthesizer"/> chokepoint above every engine
/// that corrections observability already sits at.
///
/// <para>
/// <b>PREVIEWS EXCLUDED — BY FLAG, NOT BY CONSTRUCTION</b> (SPEC F97.5, F126.1; STORY-253 AC6,
/// STORY-323). Before PLAN T274, <see cref="TtsRenderContext.Rules"/> was populated ONLY by
/// <see cref="TtsSegmentSource"/>, the real on-air render path (SPEC F97.6) — every preview caller
/// (<c>TtsPreviewController</c>) constructed a context whose <c>Rules</c> defaulted to empty, so
/// <see cref="PronunciationRuleSet.Match"/> could never return a hit for a preview render at all,
/// and no separate "is this a preview" flag was needed. PLAN T274 breaks that: the admin preview now
/// resolves the SAME station∪persona merge the air chain uses (an audition that ignored rules would
/// make the rules editor lie), so <c>matches</c> reaching this method is no longer proof of an
/// on-air render. <see cref="TtsRenderContext.IsAudition"/> now carries that distinction explicitly
/// — <see cref="Report"/> is the ONE seam that reads it, skipping BOTH the counter and the log line
/// for a render that carries it. <b>F126.5 ruling:</b> "rule hits ... log at Information" names the
/// PER-RULE HIT fact this method emits when a render is NOT an audition — it is not a demand that an
/// audition itself go unlogged; the AUDITION event (the preview request itself) DOES log its own
/// Information line, unconditionally, at <c>TtsPreviewController.Preview</c> (naming voice and
/// candidate count, never rule text) — that is simply a different fact from a rule-hit count, and
/// the two must never be conflated by sharing one counter/log line.
/// </para>
/// </summary>
public sealed class PronunciationRuleHitReporter(
    PronunciationRuleHitStats stats, ILogger<PronunciationRuleHitReporter> logger)
{
    /// <summary>
    /// Reports every rule that annotated this render's text: increments
    /// <see cref="PronunciationRuleHitStats"/> and logs one Information line per fired rule, naming
    /// the rule and the speech kind — <paramref name="kind"/> is <see langword="null"/> for a caller
    /// with no <see cref="TtsRenderContext.Kind"/> to draw one from, the same convention
    /// <see cref="TtsRenderContext"/> itself uses. Operator/persona-card-authored pattern/word text
    /// is newline-stripped before it reaches the log line (CodeQL <c>cs/log-forging</c>), mirroring
    /// <see cref="NormalizingTtsSynthesizer.ReportFiredCorrections"/>.
    ///
    /// <paramref name="isAudition"/> (SPEC F97.5, F126.1; PLAN T274) is the ONE gate on both effects
    /// below — <see langword="true"/> means this render is an operator proving a rule/pace before it
    /// airs (see <see cref="TtsRenderContext.IsAudition"/>'s own remarks for the full ruling,
    /// including the T276 sibling posture for authoring) and neither the counter nor the log line
    /// fires. Deliberately REQUIRED, no default (T274 review finding F6): a defaulted
    /// <see langword="false"/> would let a future renderer silently opt INTO counting simply by
    /// forgetting the argument — every caller states its posture explicitly, so a new call site that
    /// never considered this question fails to compile instead of quietly counting.
    /// </summary>
    public void Report(IReadOnlyList<PronunciationMatch> matches, SegmentKind? kind, bool isAudition)
    {
        if (isAudition)
            return;

        foreach (var match in matches)
        {
            stats.RecordFired(match.Rule.Pattern, match.Rule.Word);
            logger.LogInformation(
                "Pronunciation rule fired: pattern={Pattern} word={Word} kind={Kind}",
                LogSanitize.Strip(match.Rule.Pattern), LogSanitize.Strip(match.Rule.Word), kind);
        }
    }
}
