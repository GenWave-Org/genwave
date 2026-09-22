using System.Text.RegularExpressions;

namespace GenWave.Core;

/// <summary>
/// The one phone-shape regex (SPEC F197.1, PLAN T544, STORY-464 AC1/AC2) — NANP-shaped digit runs,
/// matched the SAME way everywhere in GenWave: <c>ddd-dddd</c>, <c>ddd-ddd-dddd</c>,
/// <c>(ddd) ddd-dddd</c>, with <c>-</c>/<c>.</c>/space separators, or a bare 7+ digit run.
/// <c>GenWave.Ads.PhoneShapeCheck</c> (the 555 phone rule, SPEC F160.3) references
/// <see cref="Regex"/> instead of keeping its own copy of the pattern — this is the ONE place the
/// shape is spelled out; nothing outside this file may redeclare it.
/// </summary>
public static partial class PhoneShape
{
    /// <summary>The phone-shape regex. <c>[GeneratedRegex]</c> compiles and caches the pattern once
    /// per process — every caller (today, just <c>GenWave.Ads.PhoneShapeCheck</c>) shares the
    /// same compiled instance rather than each compiling its own copy.</summary>
    public static Regex Regex => PhoneShapedRun();

    // \b sits AFTER the optional leading paren, not before it: a paren is itself a non-word
    // character, so a \b placed before it would never find a word/non-word transition when the
    // paren is actually present (space-then-paren is non-word-to-non-word). Anchoring right before
    // the first digit — wherever the optional paren left the scan position — is what forces every
    // alternative to start exactly at a digit run's own edge, so e.g. a ZIP+4 code (90210-1234)
    // never matches "210-1234" as a false 7-digit run (PLAN T399 review N8, carried over verbatim
    // from GenWave.Ads.PhoneShapeCheck at T544 — see STORY-464 AC1's four NANP-shaped facts, and
    // SPEC F197.1 for why GenWave.Ads keeps no regex of its own anymore).
    [GeneratedRegex(@"\(?\b\d{3}\)?[-.\s]\d{3}[-.\s]\d{4}\b|\b\d{3}[-.\s]\d{4}\b|\b\d{7,}\b")]
    private static partial Regex PhoneShapedRun();
}
