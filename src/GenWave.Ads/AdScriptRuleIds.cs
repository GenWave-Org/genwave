namespace GenWave.Ads;

/// <summary>
/// The <see cref="AdScriptValidator"/> rule-id vocabulary (SPEC F160.3) — snake_case tokens, stable
/// across releases: <see cref="AdScriptViolation.RuleId"/> is stored verbatim as
/// <c>station.ad_spot.fail_reason</c> (STORY-389) and surfaced verbatim in a save-time 400 (STORY-390
/// AC9), so a token here is a wire contract, not an internal label. One constant per <see
/// cref="AdScriptValidator"/> check, in the SAME first-rule-wins order that class evaluates them.
/// </summary>
public static class AdScriptRuleIds
{
    /// <summary>Wire shape: <c>TAG: line</c>, 1-3 distinct uppercase-alnum voice tags, ANNOUNCER
    /// required, per-line <c>Llm:MaxCopyChars</c> ceiling.</summary>
    public const string Format = "format";

    /// <summary>Estimated total read time exceeds the spot's duration tolerance.</summary>
    public const string Duration = "duration";

    /// <summary>The script named a blocklisted real-world brand.</summary>
    public const string BrandCollision = "brand_collision";

    /// <summary>A phone-shaped digit run does not contain 555 (no sponsor phone on file), or does not
    /// match the sponsor's own number (one is on file).</summary>
    public const string PhoneShape = "phone_shape";

    /// <summary>A profane word under the <c>everyone</c> audience posture.</summary>
    public const string AudiencePosture = "audience_posture";

    /// <summary>A parenthetical, bracketed beat, or asterisked aside survived hygiene and still
    /// appears in a line's spoken text (SPEC F201.2, STORY-468).</summary>
    public const string StageDirection = "stage_direction";
}
