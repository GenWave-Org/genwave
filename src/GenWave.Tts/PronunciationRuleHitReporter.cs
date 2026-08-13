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
/// <b>PREVIEWS EXCLUDED BY CONSTRUCTION</b> (SPEC F97.5, STORY-253 AC6): <see cref="TtsRenderContext.Rules"/>
/// is populated ONLY by <see cref="TtsSegmentSource"/>, the real on-air render path (SPEC F97.6) —
/// every preview caller (<c>TtsPreviewController</c>'s plain <c>SynthesizeAsync(text, voice, ct)</c>
/// overload, relayed through <see cref="NormalizingTtsSynthesizer"/>) constructs a context whose
/// <c>Rules</c> defaults to empty, so <see cref="PronunciationRuleSet.Match"/> can never return a
/// hit for a preview render; <see cref="KokoroTtsSynthesizer"/>'s own plain overload hardcodes
/// <see cref="PronunciationRuleSet.Empty"/> even before a context exists at all, for the identical
/// reason. No separate "is this a preview" flag exists or is needed — the same "resolved once,
/// upstream, only for a real render" boundary that already makes F97.6 hold does this exclusion's
/// whole job, so <see cref="Report"/> itself stays unconditional: it is simply never handed a
/// non-empty <c>matches</c> list on a preview render.
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
    /// </summary>
    public void Report(IReadOnlyList<PronunciationMatch> matches, SegmentKind? kind)
    {
        foreach (var match in matches)
        {
            stats.RecordFired(match.Rule.Pattern, match.Rule.Word);
            logger.LogInformation(
                "Pronunciation rule fired: pattern={Pattern} word={Word} kind={Kind}",
                LogSanitize.Strip(match.Rule.Pattern), LogSanitize.Strip(match.Rule.Word), kind);
        }
    }
}
