namespace GenWave.Tts;

/// <summary>
/// The claim classes <see cref="CopyClaims"/> can report a violation for (SPEC F138.1, F138.3): the
/// three F138.1 extracted-claim classes (<see cref="DigitRun"/>, <see cref="Weekday"/>,
/// <see cref="ConditionWord"/>) that <see cref="CopyClaims.CheckFacts"/> checks against a segment's
/// fact block, plus <see cref="Daypart"/> — a clock-only fourth class (SPEC F138.3) that
/// <see cref="CopyClaims.CheckClock"/> alone produces. Daypart is not one of F138.1's three extracted
/// claim classes and never appears in a <see cref="CopyClaims.CheckFacts"/> result; <see cref="Weekday"/>
/// is the one class shared by both entry points (a fact block can support/deny a weekday exactly like
/// a condition word, and the clock line can also confirm/deny one).
/// </summary>
public enum ClaimClass
{
    /// <summary>A run of digits (SPEC F138.1) — e.g. "21" or "108.8". <see cref="CopyClaims.CheckFacts"/> only.</summary>
    DigitRun,

    /// <summary>A weekday name (SPEC F138.1, F138.3) — e.g. "Saturday".</summary>
    Weekday,

    /// <summary>A weather-condition word (SPEC F138.1) — e.g. "sunshine". <see cref="CopyClaims.CheckFacts"/> only.</summary>
    ConditionWord,

    /// <summary>A daypart word (SPEC F138.3) — e.g. "tonight". <see cref="CopyClaims.CheckClock"/> only.</summary>
    Daypart,
}
