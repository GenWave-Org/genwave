using System.Text.RegularExpressions;
using GenWave.Core;

namespace GenWave.Ads;

/// <summary>
/// The 555 phone rule (SPEC F160.3) — any phone-number-SHAPED digit run in the RAW (never folded —
/// see <see cref="AdCopyFold"/>'s own remarks on why) script text must contain <c>555</c> somewhere in
/// its digits, or the script refuses. "Somewhere in the run" rather than pinned to the NANP exchange
/// slot is the cheap, honest read of SPEC F160.3's "must contain 555".
///
/// <para>
/// The phone SHAPE itself is <see cref="PhoneShape"/>'s (SPEC F197.1, PLAN T544) — this file keeps no
/// regex of its own and describes none; see that type's own remarks for the patterns, the separators,
/// and the <c>\b</c>-anchoring rationale. This type owns only the 555 rule applied to each match.
/// </para>
/// </summary>
internal static class PhoneShapeCheck
{
    const string RequiredDigits = "555";

    /// <summary>The first phone-shaped run (as it appeared in the raw text) that violates the rule, or
    /// <see langword="null"/> when every run clears it. WITHOUT a real <paramref name="allowedPhone"/>
    /// on file the rule is the plain one: a run violates unless it contains <c>555</c>. WITH one on file
    /// the rule tightens (SPEC F199.3, STORY-466 AC6): a run violates unless its OWN digits equal
    /// <paramref name="allowedPhone"/>'s digits exactly — a DIFFERENT run that merely contains
    /// <c>555</c> (e.g. <see cref="AdScriptWriter"/>'s own example placeholder surviving hygiene, or any
    /// other stray 555 number) is no longer exempt just for containing it. Callers check ONE LINE at a
    /// time (PLAN T399 review N8) — never a whole script joined into one string — so a digit fragment
    /// ending one line can never combine with a digit fragment opening the next into a synthesized run
    /// that existed in neither line alone.</summary>
    /// <param name="rawText">One line of raw (never folded) script text.</param>
    /// <param name="allowedPhone">The owner-sponsor phone skip (SPEC F172.5) — the sponsor's own RAW
    /// phone number, exactly as it is stored, never pre-digitized by the caller (PLAN T438 ruling: ONE
    /// normalization point, <see cref="DigitsOf"/>, rather than the caller and this method each
    /// stripping punctuation their own way). Blank or whitespace-only is treated the SAME as
    /// <see langword="null"/> — no exemption, and the plain 555 rule runs unchanged from before this
    /// parameter existed.</param>
    public static string? FindViolation(string rawText, string? allowedPhone = null)
    {
        var allowedDigits = string.IsNullOrWhiteSpace(allowedPhone) ? null : DigitsOf(allowedPhone);

        foreach (Match match in PhoneShape.Regex.Matches(rawText))
        {
            var digits = DigitsOf(match.Value);
            if (digits.Length < 7)
                continue;

            if (allowedDigits is not null)
            {
                // SPEC F199.3: a sponsor phone on file means ONLY that exact number clears this line —
                // a run that differs refuses even when it contains 555 (STORY-466 AC6), so hygiene's
                // own F199.2 rewrite (or its failure to run at all) stays honestly checked.
                if (string.Equals(digits, allowedDigits, StringComparison.Ordinal))
                    continue;

                return match.Value.Trim();
            }

            if (digits.Contains(RequiredDigits, StringComparison.Ordinal))
                continue;

            return match.Value.Trim();
        }

        return null;
    }

    /// <summary>Digits only, every other character stripped — the ONE normalization both sides of
    /// <see cref="FindViolation"/>'s digit-equality comparison run through (PLAN T438 ruling: a matched
    /// run's own text and the sponsor's raw phone number are never digitized two different ways).</summary>
    static string DigitsOf(string text) => new(text.Where(char.IsAsciiDigit).ToArray());
}
