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
    internal static bool IsShadow(IOptionsMonitor<TalkRulesOptions> options, ILogger logger)
    {
        try
        {
            return TalkRulesModes.TryParse(options.CurrentValue.Mode) == TalkRulesMode.Shadow;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TalkRules:Mode could not be read; the rule book is not called for this talk");
            return false;
        }
    }

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
