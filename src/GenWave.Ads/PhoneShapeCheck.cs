using System.Text.RegularExpressions;

namespace GenWave.Ads;

/// <summary>
/// The 555 phone rule (SPEC F160.3) — any phone-number-SHAPED digit run in the RAW (never folded —
/// see <see cref="AdCopyFold"/>'s own remarks on why) script text must contain <c>555</c> somewhere in
/// its digits, or the script refuses. "Somewhere in the run" rather than pinned to the NANP exchange
/// slot is the cheap, honest read of SPEC F160.3's "must contain 555".
///
/// <para>
/// <b>Phone shapes recognized</b> (the cheap heuristic, three patterns): a NANP-style grouping
/// (<c>(555) 123-4567</c> / <c>555-123-4567</c>), a bare local 7-digit grouping (<c>123-4567</c>), or
/// an unbroken 7+ digit run (<c>5551234567</c>). Separators are narrowly <c>-</c>, <c>.</c>, and a
/// single space — a price ("$24.99"), a year ("2026"), or "24/7" never reaches a 7-digit grouped shape
/// under any of the three patterns (a slash never joins a run; neither pattern's digit-group counts are
/// satisfied by two-to-four bare digits), so none of those false-trip.
/// </para>
///
/// <para>
/// <b>Every alternative is <c>\b</c>-anchored at both ends</b> (PLAN T399 review N8): without the
/// anchor, the 3-digit+4-digit pattern can match a SUBSTRING embedded inside a longer, unrelated digit
/// run — a ZIP+4 code (<c>90210-1234</c>) contains "210-1234" as a bare substring, which the
/// unanchored pattern happily matched. Since a genuine digit run has no internal word-boundary (every
/// digit is adjacent to another word character), anchoring forces each alternative to start and end at
/// the run's own edges, never mid-run.
/// </para>
/// </summary>
internal static partial class PhoneShapeCheck
{
    const string RequiredDigits = "555";

    /// <summary>The first phone-shaped run (as it appeared in the raw text) that does not contain
    /// <c>555</c> AND does not match the digits of <paramref name="allowedPhone"/>, or <see langword="null"/> when
    /// every phone-shaped run clears one of those two. Callers check ONE LINE at a time (PLAN T399
    /// review N8) — never a whole script joined into one string — so a digit fragment ending one line
    /// can never combine with a digit fragment opening the next into a synthesized run that existed in
    /// neither line alone.</summary>
    /// <param name="rawText">One line of raw (never folded) script text.</param>
    /// <param name="allowedPhone">The owner-sponsor phone skip (SPEC F172.5) — the sponsor's own RAW
    /// phone number, exactly as it is stored, never pre-digitized by the caller (PLAN T438 ruling: ONE
    /// normalization point, <see cref="DigitsOf"/>, rather than the caller and this method each
    /// stripping punctuation their own way). Blank or whitespace-only is treated the SAME as
    /// <see langword="null"/> — no exemption. When it normalizes to a real digit string, a phone-shaped
    /// run whose own digits equal it exactly passes even without a 555; every OTHER phone-shaped run in
    /// <paramref name="rawText"/> is still checked against the 555 rule as usual. <see langword="null"/>
    /// (the default) runs the plain 555 rule with no exemption, unchanged from before this parameter
    /// existed.</param>
    public static string? FindViolation(string rawText, string? allowedPhone = null)
    {
        var allowedDigits = string.IsNullOrWhiteSpace(allowedPhone) ? null : DigitsOf(allowedPhone);

        foreach (Match match in PhoneShapedRunRx().Matches(rawText))
        {
            var digits = DigitsOf(match.Value);
            if (digits.Length < 7 || digits.Contains(RequiredDigits, StringComparison.Ordinal))
                continue;

            if (allowedDigits is not null && string.Equals(digits, allowedDigits, StringComparison.Ordinal))
                continue; // The sponsor's own literal number (SPEC F172.5) — everything else still refuses.

            return match.Value.Trim();
        }

        return null;
    }

    /// <summary>Digits only, every other character stripped — the ONE normalization both sides of
    /// <see cref="FindViolation"/>'s digit-equality comparison run through (PLAN T438 ruling: a matched
    /// run's own text and the sponsor's raw phone number are never digitized two different ways).</summary>
    static string DigitsOf(string text) => new(text.Where(char.IsAsciiDigit).ToArray());

    // \b sits AFTER the optional leading paren, not before it: a paren is itself a non-word
    // character, so a \b placed before it would never find a word/non-word transition when the
    // paren is actually present (space-then-paren is non-word-to-non-word). Anchoring right before
    // the first digit — wherever the optional paren left the scan position — is what forces every
    // alternative to start exactly at a digit run's own edge.
    [GeneratedRegex(@"\(?\b\d{3}\)?[-.\s]\d{3}[-.\s]\d{4}\b|\b\d{3}[-.\s]\d{4}\b|\b\d{7,}\b")]
    private static partial Regex PhoneShapedRunRx();
}
