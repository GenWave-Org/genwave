namespace GenWave.Tts;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// One claim in candidate copy that <see cref="CopyClaims"/> could not support (SPEC F138.1-F138.3):
/// <see cref="Class"/> names which of the four <see cref="ClaimClass"/> subjects tripped, and
/// <see cref="Token"/> is the exact substring the checker matched in the COPY (original casing
/// preserved) — raw data for a future re-ask prompt (SPEC F138.4), not display text of its own; PLAN
/// T329's design constraint leaves rendering that prompt line to T331, deliberately.
///
/// <see cref="Expected"/> is set ONLY by <see cref="CopyClaims.CheckClock"/>, on a clock mismatch —
/// the correct weekday name (<see cref="System.DayOfWeek"/>'s own <c>ToString()</c> spelling, e.g.
/// "Sunday"), or the correct daypart category word (e.g. "morning") — so a re-ask prompt can name both
/// the mistake and the fix in one line. <see cref="CopyClaims.CheckFacts"/> never sets it: there is no
/// single "correct" fix for an unsupported fact claim, only "the fact block never said this", so it is
/// null there by construction, not by omission.
///
/// <para>
/// <b>Safe to interpolate (T329 review round 1 finding):</b> <see cref="Token"/> is provably
/// closed-vocabulary-or-digit-shaped — it can only ever be a digit run, one of
/// <see cref="ClaimVocabulary.WeekdayAlternation"/>'s seven names, one of
/// <see cref="ClaimVocabulary.ConditionWordAlternation"/>'s words, or one of
/// <see cref="ClaimVocabulary.DaypartWordAlternation"/>'s words — so a future re-ask prompt (SPEC
/// F138.4, PLAN T331) may interpolate it directly into prompt text without fence-forging risk; rely on
/// it knowingly rather than re-deriving the guarantee at the call site.
/// </para>
/// </summary>
public sealed record ClaimViolation(ClaimClass Class, string Token, string? Expected = null)
{
    /// <summary>
    /// True when this violation carries a correct-value fix (<see cref="Expected"/> set) — i.e. it
    /// came from <see cref="CopyClaims.CheckClock"/>, never <see cref="CopyClaims.CheckFacts"/> (see
    /// this record's own remarks above on which checker sets <see cref="Expected"/> and why). PLAN
    /// T332 review round-2 finding: <c>LlmCopyWriter.DescribeViolationForLog</c> and
    /// <c>LlmPromptBuilder.DescribeViolationForReask</c> both key their own clock-vs-facts split on
    /// this ONE property now, rather than each independently re-deriving "is this a clock violation"
    /// from <see cref="Expected"/>'s own nullability at two separate call sites across a module
    /// boundary — a duplication that could silently drift the day either one grew a different (wrong)
    /// test.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Expected))]
    public bool IsClockClaim => Expected is not null;
}
