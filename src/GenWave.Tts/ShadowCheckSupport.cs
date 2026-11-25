using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenWave.Tts;

/// <summary>
/// What every shadow hook shares (SPEC F220.18, F220.19): reading the live mode, the time one check may take, and how a
/// legacy outcome reads as a rule-book outcome. The hooks differ only in the snapshot they build.
/// </summary>
internal static class ShadowCheckSupport
{
    /// <summary>The longest one check may take before it is abandoned with a Warning.</summary>
    internal static readonly TimeSpan JudgeBudget = TimeSpan.FromSeconds(2);

    /// <summary>
    /// True when the live mode is shadow, read fresh each call so a PUT applies to the next written talk (F220.18). An
    /// unreadable value (a live edit that fails validation) reads as legacy. Never throws.
    /// </summary>
    internal static bool IsShadow(IOptionsMonitor<TalkRulesOptions> options, ILogger logger) =>
        IsMode(options, logger, TalkRulesMode.Shadow, "the rule book is not called for this talk");

    /// <summary>As <see cref="IsShadow"/>, for rules mode (F220.10): an unreadable value reads as legacy. Never throws.</summary>
    internal static bool IsRules(IOptionsMonitor<TalkRulesOptions> options, ILogger logger) =>
        IsMode(options, logger, TalkRulesMode.Rules, "this talk is written the legacy way");

    static bool IsMode(IOptionsMonitor<TalkRulesOptions> options, ILogger logger, TalkRulesMode wanted, string consequence)
    {
        try
        {
            return TalkRulesModes.TryParse(options.CurrentValue.Mode) == wanted;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TalkRules:Mode could not be read; {Consequence}", consequence);
            return false;
        }
    }

    /// <summary>
    /// What a DJ-line attempt with no usable text came to, whichever mode decided it. A preview never templates:
    /// <c>WritePreviewAsync</c> turns the null reply into a failed preview and nothing airs, so Dropped whatever the kind. Else kinds
    /// that never air template copy are dropped (<see cref="TemplateCopyPolicy"/>, the same rule <c>TtsSegmentSource</c> drops by),
    /// the rest fall to the template rung. Announcement is the exception: a null return airs the owner's plain copy
    /// (<c>BreakRenderer</c>), so something does air from fallback, i.e. Template.
    /// </summary>
    internal static TalkLegacyOutcome NoTextOutcome(SegmentRequest request, bool preview) =>
        preview || (request.Kind != SegmentKind.Announcement && TemplateCopyPolicy.NeverAirsTemplateCopy(request.Kind))
            ? TalkLegacyOutcome.Dropped
            : TalkLegacyOutcome.Template;

    /// <summary>What the attempt came to, read off what today's code did with it.</summary>
    internal static TalkOutcome OutcomeOf(TalkLegacyOutcome legacy) => legacy switch
    {
        TalkLegacyOutcome.Passed or TalkLegacyOutcome.Trimmed => TalkOutcome.Accepted,
        TalkLegacyOutcome.ReAsked => TalkOutcome.ReAsked,
        TalkLegacyOutcome.Template => TalkOutcome.Template,
        TalkLegacyOutcome.Dropped => TalkOutcome.Dropped,
        _ => throw new ArgumentOutOfRangeException(nameof(legacy), legacy, "Unknown legacy outcome."),
    };
}
