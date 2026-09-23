using System.Text.RegularExpressions;

namespace GenWave.Ads;

/// <summary>
/// The format stage of <see cref="AdScriptValidator"/> (SPEC F160.3, STORY-390 AC1/AC8) — the
/// <c>CrosstalkScriptParser</c> shape narrowed to the ad wire format: <c>TAG: line</c>, 1-3 DISTINCT
/// uppercase-alphanumeric voice tags AFTER FOLDING (SPEC F200.1/F200.2 — see this class's own "not
/// every tag is a voice" remarks below), <see cref="AnnouncerTag"/> required, each line's text bounded
/// by the caller's per-line char ceiling. Fail-closed, first-rule-wins: the first line/rule that breaks
/// the shape is the reason returned, never a full list.
///
/// <para>
/// <b>Plain sentences are accepted too</b> (SPEC F174.6, PLAN T444 ruling): before any line's text is
/// checked, <see cref="Parse"/> classifies every non-blank line as tagged or untagged (see
/// <see cref="IsTaggedLine"/>) and applies exactly one of three outcomes. Every line untagged: each
/// becomes an <see cref="AnnouncerTag"/> line, verbatim — no split is attempted on an untagged line, so
/// an interior colon inside a plain sentence never truncates its text — and the same per-line budget,
/// same empty-text check (never triggered here, since a blank line was already dropped before this
/// classification runs) that a hand-tagged line gets applies identically. Every line tagged: nothing
/// about the parse below changes. Some tagged, some not: refused with
/// <see cref="AdScriptRuleIds.Format"/> and reason
/// <c>"line {N} has no voice tag while others do"</c>, where <c>N</c> is the 1-based position of the
/// FIRST untagged line among the NON-BLANK lines (blanks are dropped before this numbering runs, so a
/// blank interior line never shifts it).
/// </para>
///
/// <para>
/// <b>Not every <see cref="TagPattern"/>-shaped tag is a voice</b> (SPEC F200.1, STORY-467): only
/// <see cref="KnownTags"/> — <see cref="AnnouncerTag"/>, <c>VOICE1</c>, <c>VOICE2</c> — cast a distinct
/// voice. A tag that matched <see cref="TagPattern"/> but is not in <see cref="KnownTags"/> (e.g.
/// <c>NARRATOR</c>, <c>VOICE 2</c> once its space fails the pattern) folds onto
/// <see cref="AnnouncerTag"/> instead of refusing — its copy is kept, attributed to the announcer — and
/// <see cref="Parse"/> records one <see cref="AdScript.Notes"/> entry per DISTINCT unknown tag
/// (<c>FoldUnknownTags</c>). <see cref="MinVoiceTags"/>/<see cref="MaxVoiceTags"/> and the "no
/// <see cref="AnnouncerTag"/> line" rule both run over the FOLDED lines (SPEC F200.2), so a script that
/// is entirely unknown tags is a valid one-voice, all-<see cref="AnnouncerTag"/> script.
/// </para>
/// </summary>
internal static partial class AdScriptParser
{
    /// <summary>The one voice tag every script must carry (SPEC F160.3).</summary>
    public const string AnnouncerTag = "ANNOUNCER";

    const int MinVoiceTags = 1;
    /// <summary>F160.3's "1–3" upper bound, kept as a documented invariant: with <see cref="KnownTags"/>
    /// at exactly three and every other tag folding (F200.2), the <c>&gt; MaxVoiceTags</c> arm is only
    /// reachable if the known cast ever grows past three.</summary>
    const int MaxVoiceTags = 3;

    /// <summary>The full known cast (SPEC F200.1) — <see cref="AdCastPicker.Voice1Tag"/>/
    /// <see cref="AdCastPicker.Voice2Tag"/> are the SAME two tags <c>AdScriptPromptBuilder</c> tells the
    /// writing model about, duplicated here as the one place the parser itself needs to know which tags
    /// are cast, never merely shape-matched. Any other <see cref="TagPattern"/>-shaped tag folds onto
    /// <see cref="AnnouncerTag"/> (<c>FoldUnknownTags</c>) rather than refusing.</summary>
    static readonly IReadOnlySet<string> KnownTags =
        new HashSet<string>(StringComparer.Ordinal) { AnnouncerTag, AdCastPicker.Voice1Tag, AdCastPicker.Voice2Tag };

    public static AdScriptValidationResult Parse(string rawScript, int maxLineChars)
    {
        // Blank interior lines are skipped, never refused (the CrosstalkScriptParser precedent, PLAN
        // T399 review N5) — accidental double-spacing between beats is a common LLM formatting quirk,
        // not a shape violation worth burning a re-ask on.
        var rawLines = rawScript.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        if (rawLines.Count == 0)
            return Refused("the script has no lines");

        var (candidateLines, mixedTagsViolation) = ApplyPlainSentencePrePass(rawLines);
        if (mixedTagsViolation is not null)
            return new AdScriptValidationResult.Refused(mixedTagsViolation);

        var lines = new List<AdScriptLine>(candidateLines.Count);
        foreach (var (tag, text) in candidateLines)
        {
            var checkedLine = CheckLine(tag, text, maxLineChars);
            if (checkedLine is (AdScriptLine line, null))
            {
                lines.Add(line);
                continue;
            }

            if (checkedLine is (null, AdScriptViolation violation))
                return new AdScriptValidationResult.Refused(violation);
        }

        var (foldedLines, notes) = FoldUnknownTags(lines);

        var distinctTags = foldedLines.Select(line => line.Tag).Distinct(StringComparer.Ordinal).ToList();
        if (distinctTags.Count is < MinVoiceTags or > MaxVoiceTags)
            return Refused($"expected {MinVoiceTags}-{MaxVoiceTags} distinct voice tags, got {distinctTags.Count}");

        if (!distinctTags.Contains(AnnouncerTag, StringComparer.Ordinal))
            return Refused($"no {AnnouncerTag} line appeared — every spot needs the {AnnouncerTag} voice");

        return new AdScriptValidationResult.Accepted(new AdScript(foldedLines, notes));
    }

    /// <summary>Folds every line whose tag is not in <see cref="KnownTags"/> onto
    /// <see cref="AnnouncerTag"/> (SPEC F200.1) — the line's own <see cref="AdScriptLine.Text"/> is kept
    /// verbatim, only its <see cref="AdScriptLine.Tag"/> changes. One note per DISTINCT original tag, in
    /// first-seen order (SPEC F200.1: "a tag appearing on three lines yields one note"); the tag echoed
    /// through <see cref="AdScriptEcho.ForReason"/>, the same untrusted-echo bound every other
    /// logged/surfaced tag in this class goes through.</summary>
    static (IReadOnlyList<AdScriptLine> Lines, IReadOnlyList<string> Notes) FoldUnknownTags(
        IReadOnlyList<AdScriptLine> lines)
    {
        var foldedLines = new List<AdScriptLine>(lines.Count);
        var notes = new List<string>();
        var seenUnknownTags = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            if (KnownTags.Contains(line.Tag))
            {
                foldedLines.Add(line);
                continue;
            }

            foldedLines.Add(line with { Tag = AnnouncerTag });
            if (seenUnknownTags.Add(line.Tag))
                notes.Add($"unknown-tag:{AdScriptEcho.ForReason(line.Tag)}");
        }

        return (foldedLines, notes);
    }

    /// <summary>The plain-sentence pre-pass itself (SPEC F174.6, PLAN T444 ruling): classifies every
    /// already-trimmed, non-blank <paramref name="rawLines"/> entry with <see cref="IsTaggedLine"/> and
    /// picks one of three outcomes, returning candidates already split into (tag, text) pairs — the
    /// grammar check below (<see cref="CheckLine"/>) never re-splits a line, it only validates the text
    /// it is handed. Zero tagged: every entry pairs with <see cref="AnnouncerTag"/> and its own
    /// UNSPLIT, verbatim line as the text (an interior colon inside a plain sentence is never treated
    /// as a tag boundary). All tagged: each entry is <see cref="IsTaggedLine"/>'s own split at that
    /// line's first colon. Some of each: <c>Violation</c> carries the refusal and <c>Lines</c> is empty
    /// (unused by the caller in that branch — an empty list rather than a null read at the call
    /// site).</summary>
    static (IReadOnlyList<(string Tag, string Text)> Lines, AdScriptViolation? Violation) ApplyPlainSentencePrePass(
        IReadOnlyList<string> rawLines)
    {
        var splitLines = rawLines.Select(IsTaggedLine).ToList();
        var taggedCount = splitLines.Count(split => split.Tagged);

        if (taggedCount == 0)
            return (rawLines.Select(line => (AnnouncerTag, line)).ToList(), null);

        if (taggedCount == rawLines.Count)
            return (splitLines.Select(split => (split.Tag, split.Text)).ToList(), null);

        // Blanks were dropped from rawLines before this list was built, so this 1-based position
        // already counts only the non-blank lines (PLAN T444 ruling) — a blank interior line never
        // shifts which line number a reason names.
        var firstUntaggedLineNumber = splitLines.FindIndex(split => !split.Tagged) + 1;
        return (
            Array.Empty<(string Tag, string Text)>(),
            FormatViolation($"line {firstUntaggedLineNumber} has no voice tag while others do"));
    }

    /// <summary>"Tagged" per SPEC F174.6/PLAN T444 ruling: the line carries a colon at index > 0 AND
    /// the text before that FIRST colon, trimmed, matches <see cref="TagPattern"/>. A lowercase-led
    /// colon inside an ordinary sentence ("Open at six: worth it") is therefore untagged. A known edge
    /// this same grammar accepts: an all-caps word before a colon inside what reads as a plain sentence
    /// ("WOW: yes") IS tagged — the 1-3-distinct-tags and <see cref="AnnouncerTag"/>-required rules
    /// further down still govern it exactly as they would any other voice tag. When the line IS tagged,
    /// this also returns the (Tag, Text) split at that same first colon (PLAN T444 ruling) — the sole
    /// place that split happens, so a tagged line is never scanned for its colon twice.</summary>
    static (bool Tagged, string Tag, string Text) IsTaggedLine(string trimmedLine)
    {
        var colonIndex = trimmedLine.IndexOf(':');
        if (colonIndex <= 0)
            return (false, string.Empty, string.Empty);

        var tag = trimmedLine[..colonIndex].Trim();
        return TagPattern().IsMatch(tag)
            ? (true, tag, trimmedLine[(colonIndex + 1)..].Trim())
            : (false, string.Empty, string.Empty);
    }

    /// <summary>Checks one already tag/text-split candidate — never both a line and a violation, never
    /// neither (PLAN T399 review N2: a plain tuple return, no out-param, no reason-that-cannot-
    /// actually-be-null coalesce at the call site). PLAN T444 ruling: the tag's shape is no longer
    /// checked here — every candidate reaching this method already carries either a tag that matched
    /// <see cref="TagPattern"/> (<see cref="IsTaggedLine"/>'s split) or the synthetic
    /// <see cref="AnnouncerTag"/> the all-untagged pre-pass supplies — so only the text is
    /// checked.</summary>
    static (AdScriptLine? Line, AdScriptViolation? Violation) CheckLine(string tag, string text, int maxLineChars)
    {
        if (text.Length == 0)
            return (null, FormatViolation($"the {AdScriptEcho.ForReason(tag)} line has no spoken text"));

        if (text.Length > maxLineChars)
            return (null, FormatViolation($"the {AdScriptEcho.ForReason(tag)} line ({text.Length} chars) exceeds the {maxLineChars}-char per-line budget"));

        return (new AdScriptLine(tag, text), null);
    }

    static AdScriptValidationResult.Refused Refused(string reason) => new(FormatViolation(reason));

    static AdScriptViolation FormatViolation(string reason) => new(AdScriptRuleIds.Format, reason);

    // Must start with a letter (PLAN T399 review N4) — a digits-only tag ("12") is not a plausible
    // voice name, so a line whose would-be tag is pure digits reads as malformed FORMAT rather than
    // silently accepting a nonsense tag. A digit sequence appearing later, inside a line's spoken
    // TEXT (e.g. "ANNOUNCER: It's 12:30..."), is untouched — only the FIRST colon ever splits tag
    // from text, so a second colon deeper in the text is just text.
    [GeneratedRegex(@"^[A-Z][A-Z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();
}
