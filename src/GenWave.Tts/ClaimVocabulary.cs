namespace GenWave.Tts;

/// <summary>
/// The versioned, curated vocabularies <see cref="CopyClaims"/> matches against (SPEC F138.1,
/// F138.3) — plain data, no I/O, no settings: the same purity posture as the checker itself. Curated,
/// not exhaustive by design (see <see cref="CopyClaims"/>'s own false-positive-posture remarks): a
/// real weekday/condition/daypart word missing from one of these lists is simply never extracted as a
/// claim at all, which is the SAFE gap to have — it can neither wrongly pass nor wrongly fail, it is
/// just invisible to the checker, the same as any word outside these three subjects already is.
///
/// <para>
/// Each list below is exposed as a single pipe-delimited <c>internal const string</c> "vN" alternation
/// — the one canonical source of truth <see cref="CopyClaims"/>'s <c>[GeneratedRegex]</c> extraction
/// patterns interpolate directly (a compile-time-constant expression), so a matching regression can
/// never drift from the vocabulary that produced it. There is no parallel <c>IReadOnlyList&lt;string&gt;</c>
/// view of any of these anymore (T329 review round 2): nothing outside the regex patterns themselves
/// ever needed one, and a second, unread copy is exactly the kind of drift risk this file exists to
/// avoid. Bump the "vN" marker in a list's own remarks whenever an entry is added or removed, so a
/// matching regression can be traced to a vocabulary change rather than a logic change.
/// </para>
/// </summary>
static class ClaimVocabulary
{
    /// <summary>
    /// v1 (PLAN T329, 2026-08-20) — the seven Gregorian weekday names, English only, lowercase
    /// (matching is always case-insensitive; every consumer applies <see cref="System.Text.RegularExpressions.RegexOptions.IgnoreCase"/>
    /// or an explicit ordinal-ignore-case comparison, never relying on this casing itself).
    /// </summary>
    internal const string WeekdayAlternation = "sunday|monday|tuesday|wednesday|thursday|friday|saturday";

    /// <summary>
    /// v2 (T329 review round 2, 2026-08-20) — dropped <c>clear</c> from v1's set. Its dominant sense in
    /// DJ prose is not weather at all ("let me be clear", "clear away the cobwebs", "make it clear") —
    /// far more common on air than "clear skies" — so it was producing condition-word false claims on
    /// ordinary, weather-free copy. The remaining words below are kept even though each has its own
    /// well-known non-weather sense too (a metaphorical "storm of great music", the movie "Purple
    /// Rain", the band "Snow Patrol") — that risk is accepted (the false-positive posture: a spurious
    /// re-ask costs one retry, never silence) because those words are still weather-dominant in typical
    /// DJ copy, unlike "clear"; a future vocabulary edit should read this remark before adding another
    /// word back rather than re-litigating the same call from scratch.
    /// </summary>
    internal const string ConditionWordAlternation =
        "sunny|sunshine|rain|rainy|rainfall|overcast|cloudy|snow|snowy|storm|stormy|" +
        "thunderstorm|thunderstorms|foggy|fog|windy|drizzle|drizzly|hail|sleet|humid|mist|misty";

    /// <summary>
    /// v1 (PLAN T329, 2026-08-20) — the daypart word set (SPEC F138.3's own stated minimum:
    /// morning/afternoon/evening/tonight/night). "tonight" and "night" name the SAME daypart CATEGORY
    /// (see <see cref="CategoryOf"/>) — gh-#438's own exhibit used "Tonight", and a listener hears the
    /// two as interchangeable.
    /// </summary>
    internal const string DaypartWordAlternation = "morning|afternoon|evening|tonight|night";

    /// <summary>
    /// Canonical daypart CATEGORY for a daypart word (SPEC F138.3): identity for every word except
    /// "tonight", which folds onto "night" (see <see cref="DaypartWordAlternation"/>'s own remarks).
    /// <see cref="CopyClaims.CheckClock"/> compares/dedupes by category, never raw words, so "tonight"
    /// and "night" are always treated as the one claim.
    /// </summary>
    public static string CategoryOf(string daypartWord) =>
        string.Equals(daypartWord, "tonight", StringComparison.OrdinalIgnoreCase)
            ? "night"
            : daypartWord.ToLowerInvariant();

    /// <summary>
    /// True when daypart CATEGORY <paramref name="category"/>'s own window (SPEC F138.3, amended T329
    /// review round 1 — a broadcast-dayparting convention, not a physical law) includes station-local
    /// hour <paramref name="hour"/> (24-hour clock, 0-23 — the same range
    /// <see cref="System.DateTimeOffset.Hour"/> already returns). Windows OVERLAP rather than
    /// partition — a claim passes if the clock hour falls in ANY window the claimed word's own
    /// category names, so "Good evening" and "Good night" are both true statements at 21:00, and
    /// neither one is a lie:
    /// <list type="bullet">
    /// <item>Morning: 05:00-11:59</item>
    /// <item>Afternoon: 12:00-17:59</item>
    /// <item>Evening: 17:00-22:59</item>
    /// <item>Night: 21:00-04:59 (wraps past midnight — the small hours read as "night", not "morning")</item>
    /// </list>
    /// <see cref="CopyClaims.CheckClock"/> calls this once per claimed word's own category — never a
    /// single "the" category for an hour; see <see cref="CategoryForHour"/> for that single-category
    /// mapping, kept ONLY to fill <see cref="ClaimViolation.Expected"/> on a genuine mismatch.
    /// </summary>
    public static bool HourIsInCategory(string category, int hour) => category switch
    {
        "morning" => hour is >= 5 and <= 11,
        "afternoon" => hour is >= 12 and <= 17,
        "evening" => hour is >= 17 and <= 22,
        "night" => hour is >= 21 and <= 23 or >= 0 and <= 4,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown daypart category."),
    };

    /// <summary>
    /// The single canonical daypart CATEGORY for a station-local hour (SPEC F138.3, 24-hour clock,
    /// 0-23) — a non-overlapping PARTITION, unlike <see cref="HourIsInCategory"/>'s overlapping
    /// windows above. <see cref="CopyClaims.CheckClock"/> uses this ONLY to fill
    /// <see cref="ClaimViolation.Expected"/> on a genuine mismatch (a re-ask prompt needs exactly one
    /// "the correct answer is X" to name, not a set); it never drives the pass/fail decision itself.
    /// Boundaries here match <see cref="HourIsInCategory"/>'s own windows' non-overlapping halves.
    /// </summary>
    public static string CategoryForHour(int hour) => hour switch
    {
        >= 5 and <= 11 => "morning",
        >= 12 and <= 16 => "afternoon",
        >= 17 and <= 20 => "evening",
        >= 21 and <= 23 => "night",
        >= 0 and <= 4 => "night",
        _ => throw new ArgumentOutOfRangeException(nameof(hour), hour, "Station-local hour must be 0-23."),
    };
}
