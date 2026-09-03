using System.Text.RegularExpressions;

namespace GenWave.Ads;

/// <summary>
/// The format stage of <see cref="AdScriptValidator"/> (SPEC F160.3, STORY-390 AC1/AC8) — the
/// <c>CrosstalkScriptParser</c> shape narrowed to the ad wire format: <c>TAG: line</c>, 1-3 DISTINCT
/// uppercase-alphanumeric voice tags, <see cref="AnnouncerTag"/> required, each line's text bounded by
/// the caller's per-line char ceiling. Fail-closed, first-rule-wins: the first line/rule that breaks
/// the shape is the reason returned, never a full list.
/// </summary>
internal static partial class AdScriptParser
{
    /// <summary>The one voice tag every script must carry (SPEC F160.3).</summary>
    public const string AnnouncerTag = "ANNOUNCER";

    const int MinVoiceTags = 1;
    const int MaxVoiceTags = 3;

    /// <summary>Cap for a raw line/tag echoed into a violation reason (the CrosstalkScriptParser
    /// <c>MaxEchoedLineChars</c> precedent, F127.11, PLAN T399 review F6) — an untrusted script's raw
    /// text reaches a Reason that is logged and surfaced verbatim (STORY-390 AC9's 400), never an
    /// unbounded echo.</summary>
    const int MaxEchoedChars = 120;

    public static AdScriptValidationResult Parse(string rawScript, int maxLineChars)
    {
        // Blank interior lines are skipped, never refused (the CrosstalkScriptParser precedent, PLAN
        // T399 review N5) — accidental double-spacing between beats is a common LLM formatting quirk,
        // not a shape violation worth burning a re-ask on.
        var rawLines = rawScript.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        if (rawLines.Count == 0)
            return Refused("the script has no lines");

        var lines = new List<AdScriptLine>(rawLines.Count);
        foreach (var rawLine in rawLines)
        {
            var parsed = ParseLine(rawLine, maxLineChars);
            if (parsed is (AdScriptLine line, null))
            {
                lines.Add(line);
                continue;
            }

            if (parsed is (null, AdScriptViolation violation))
                return new AdScriptValidationResult.Refused(violation);
        }

        var distinctTags = lines.Select(line => line.Tag).Distinct(StringComparer.Ordinal).ToList();
        if (distinctTags.Count is < MinVoiceTags or > MaxVoiceTags)
            return Refused($"expected {MinVoiceTags}-{MaxVoiceTags} distinct voice tags, got {distinctTags.Count}");

        if (!distinctTags.Contains(AnnouncerTag, StringComparer.Ordinal))
            return Refused($"no {AnnouncerTag} line appeared — every spot needs the {AnnouncerTag} voice");

        return new AdScriptValidationResult.Accepted(new AdScript(lines));
    }

    /// <summary>Parses one non-blank raw line into either a line or a violation — never both, never
    /// neither (PLAN T399 review N2: a plain tuple return, no out-param, no reason-that-cannot-
    /// actually-be-null coalesce at the call site).</summary>
    static (AdScriptLine? Line, AdScriptViolation? Violation) ParseLine(string trimmedLine, int maxLineChars)
    {
        var colonIndex = trimmedLine.IndexOf(':');
        if (colonIndex <= 0)
            return (null, FormatViolation($"line does not match the 'TAG: line' format: \"{EchoForReason(trimmedLine)}\""));

        var tag = trimmedLine[..colonIndex].Trim();
        var text = trimmedLine[(colonIndex + 1)..].Trim();

        if (!TagPattern().IsMatch(tag))
            return (null, FormatViolation($"voice tag \"{EchoForReason(tag)}\" is not uppercase-alphanumeric, starting with a letter"));

        if (text.Length == 0)
            return (null, FormatViolation($"the {EchoForReason(tag)} line has no spoken text"));

        if (text.Length > maxLineChars)
            return (null, FormatViolation($"the {EchoForReason(tag)} line ({text.Length} chars) exceeds the {maxLineChars}-char per-line budget"));

        return (new AdScriptLine(tag, text), null);
    }

    static AdScriptValidationResult.Refused Refused(string reason) => new(FormatViolation(reason));

    static AdScriptViolation FormatViolation(string reason) => new(AdScriptRuleIds.Format, reason);

    /// <summary>Bounds an untrusted raw echo to <see cref="MaxEchoedChars"/> and strips control
    /// characters (CWE-117 log forging — PLAN T399 review F6) before it ever reaches a Reason
    /// string.</summary>
    static string EchoForReason(string text)
    {
        var stripped = text.Any(char.IsControl) ? new string(text.Where(c => !char.IsControl(c)).ToArray()) : text;
        return stripped.Length <= MaxEchoedChars ? stripped : stripped[..MaxEchoedChars] + "…";
    }

    // Must start with a letter (PLAN T399 review N4) — a digits-only tag ("12") is not a plausible
    // voice name, so a line whose would-be tag is pure digits reads as malformed FORMAT rather than
    // silently accepting a nonsense tag. A digit sequence appearing later, inside a line's spoken
    // TEXT (e.g. "ANNOUNCER: It's 12:30..."), is untouched — only the FIRST colon ever splits tag
    // from text, so a second colon deeper in the text is just text.
    [GeneratedRegex(@"^[A-Z][A-Z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();
}
