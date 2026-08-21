namespace GenWave.Tts;

using System.Text.RegularExpressions;
using GenWave.Core.Domain;

/// <summary>
/// Strict parse + validation for a <see cref="CrosstalkScriptWriter"/> completion reply (SPEC F127.3,
/// F127.4, F138.6, STORY-326 AC2, AC3, AC4, AC6, STORY-352). Fail-closed by construction: the FIRST
/// rule a reply breaks is the one returned — no partial credit, no salvage, no template rung (F127.4's
/// "the failure mode is skip"). <see cref="HostTag"/>/<see cref="NeighborTag"/>/<see cref="InterjectionMarker"/>
/// are the single source of truth for the wire format — <see cref="CrosstalkPromptBuilder"/> states the
/// exact same three tokens in the instructions it builds, so the model is never asked to emit a shape
/// this parser doesn't also accept.
///
/// <para>
/// <b>The F138.6 truth discard reasons (PLAN T333):</b> once a reply clears every SHAPE rule above (both
/// speakers present, alternation, per-line hygiene/budget), it must also clear four MECHANICAL truth
/// checks — <see cref="TruthShapeChecks"/>'s own <see cref="FrequencyRx"/>/<see cref="CallSignRx"/>
/// (frequency/call-sign shapes), <see cref="CopyClaims.ConditionWordRx"/> (the SAME F138.1 vocabulary,
/// no second list), <see cref="DateClaimRx"/> (digit-date shapes), and <see cref="CopyClaims.CheckClock"/>
/// (the T329 present-frame clock predicate, reused verbatim — see <see cref="Parse"/>'s own
/// <paramref name="stationLocalNow"/> remarks for the shared generation-time instant). Every one of
/// these is a SHAPE a checker can verify mechanically.
/// Real-geography invention — a fabricated city, venue, or landmark — is deliberately NOT checked here:
/// F138.6 states plainly that a checker cannot tell a real place from an invented one, so that half of
/// the anti-fabrication rule lives ONLY in <see cref="CrosstalkPromptBuilder"/>'s own prompt clause, never
/// pretended into a regex here. A truth-check failure stamps <see cref="LlmCallCause.TruthGateReject"/> —
/// never <see cref="LlmCallCause.MalformedResponse"/> (the shape was fine; the CONTENT is the problem)
/// and never a re-ask (F127.4 has none for crosstalk — a truth discard is silent, and the stock worker
/// tries again on its own cadence, exactly like every other discard this method returns).
/// </para>
///
/// <para>
/// Produces <see cref="CrosstalkAiredScript"/>/<see cref="CrosstalkAiredLine"/> directly (round-2
/// review F8) — the SAME published GenWave.Abstractions shape <c>GenWave.Orchestration</c>/
/// <c>GenWave.MediaLibrary</c> carry a validated script forward on, so no second, GenWave.Tts-local
/// script/line pair (and no mapper between the two) is needed. See
/// <see cref="CrosstalkSpeaker"/>'s own remarks for why the shared enum lives in Abstractions.
/// </para>
/// </summary>
static partial class CrosstalkScriptParser
{
    /// <summary>The literal line prefix a HOST turn is tagged with. Deliberately a fixed ROLE token,
    /// never the persona's own display name — a card whose <c>Name</c> contains a colon, a space, or
    /// changes between deploys must never destabilize the parse.</summary>
    public const string HostTag = "HOST";

    /// <summary>The literal line prefix a NEIGHBOR turn is tagged with — see <see cref="HostTag"/>'s own remarks.</summary>
    public const string NeighborTag = "NEIGHBOR";

    /// <summary>
    /// Appended to a speaker tag (before the colon) to mark a line as an interjection (SPEC F127.3,
    /// F127.6) — e.g. <c>"HOST (interjects): ..."</c>. Matched case-insensitively, with optional
    /// surrounding whitespace, by <see cref="TryParseLine"/>.
    /// </summary>
    public const string InterjectionMarker = "(interjects)";

    /// <summary>Fewest speaker-tagged lines a script may carry (SPEC F127.4).</summary>
    public const int MinLines = 3;

    /// <summary>Most speaker-tagged lines a script may carry (SPEC F127.4).</summary>
    public const int MaxLines = 8;

    /// <summary>
    /// Cap for a raw model line echoed into a discard reason (T282 review finding, F127.4/F127.11):
    /// that reason reaches an Information log line AND <see cref="LlmCallRecord.StatusDetail"/>
    /// (the <c>/api/llm-calls</c> ring), neither of which is the debug surface for a whole raw
    /// reply — <see cref="LlmCopyWriter"/> never logs a raw reply at Information either (see that
    /// class's own <c>LogFailure</c> remarks: "excludes the prompt itself"). ~120 chars is enough
    /// to show an operator WHICH line broke without echoing a whole unbounded model line into a log
    /// line/ring entry.
    /// </summary>
    const int MaxEchoedLineChars = 120;

    /// <summary>
    /// Spoken-rate constant for the F127.4 duration estimate — words/characters spoken per second of
    /// air, the SAME 15 chars/sec cold-tier heuristic <c>GenWave.Orchestration.RollingPatterDurationEstimator</c>
    /// already uses for ordinary patter (duplicated, not referenced: that project cannot depend on
    /// this one, the identical L1/L5 layering reason that estimator's own remarks give for
    /// duplicating <c>LlmOptions.MaxCopyChars</c>'s default rather than reading it live). Using the
    /// SAME figure keeps "how long will this run" answered consistently across every spoken-copy
    /// estimate in the codebase rather than two independently-tuned guesses.
    /// </summary>
    internal const double CharsPerSecond = 15.0;

    /// <summary>
    /// Parses and fully validates one completion reply into a <see cref="CrosstalkAiredScript"/> (SPEC
    /// F127.3, F127.4, F138.6). <paramref name="maxLineChars"/> is the per-line char budget (the SAME
    /// <c>Llm:MaxCopyChars</c> ceiling an ordinary blurb carries — no second setting); a line over it
    /// discards the WHOLE exchange, never a trim (F127.4). <paramref name="durationTargetSeconds"/> is
    /// the live <see cref="CrosstalkOptions.DurationTargetSeconds"/> value. <paramref name="stationLocalNow"/>
    /// (PLAN T333) is the SAME generation-time instant <see cref="CrosstalkScriptWriter.WriteExchangeAsync"/>
    /// already threads into <see cref="LlmPromptBuilder.BuildStationClockLine"/> for this exact request
    /// (<see cref="CrosstalkExchangeRequest.StationLocalNow"/>) — never a freshly-read clock, so the
    /// F138.3 clock check below provably judges the script against the SAME clock the prompt stated,
    /// the identical one-shared-instant discipline <see cref="CopyClaims.CheckClock"/>'s own remarks
    /// require of every other patter kind.
    /// </summary>
    public static CrosstalkWriteResult Parse(
        string rawResponse, int maxLineChars, int durationTargetSeconds, DateTimeOffset stationLocalNow)
    {
        var rawLines = rawResponse
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        if (rawLines.Count is < MinLines or > MaxLines)
        {
            // SPEC F139.1 (T330 review round 1 amendment): a malformed-SHAPE reject — the reply came
            // back with content, it just never fit the required line count (an over-MaxLines reply is
            // the amendment's own exhibit: TOO MUCH content is not "empty" by any honest reading).
            return Discarded(
                $"expected {MinLines}-{MaxLines} speaker-tagged lines, got {rawLines.Count}", LlmCallCause.MalformedResponse);
        }

        var lines = new List<CrosstalkAiredLine>(rawLines.Count);
        foreach (var rawLine in rawLines)
        {
            if (!TryParseLine(rawLine, out var speaker, out var isInterjection, out var rawText))
            {
                // SPEC F139.1 (T330 review round 1 amendment): an unrecognized speaker tag is a
                // malformed SHAPE, not empty content.
                return Discarded(
                    $"line does not match the '{HostTag}:'/'{NeighborTag}:' speaker-tag format: " +
                    $"\"{TruncateForEcho(rawLine)}\"", LlmCallCause.MalformedResponse);
            }

            var cleaned = LlmCopyWriter.ApplyCopyHygiene(rawText);
            if (cleaned.Length == 0)
            {
                // SPEC F139.1 (T330 review round 1 amendment): the ONE parser reject that STAYS
                // EmptyCompletion — the tag matched correctly (the SHAPE was fine), only the text
                // after it was empty. See LlmCallCause's own remarks for why this is the deliberate
                // holdout among the parser's reject branches.
                return Discarded($"a {DescribeSpeaker(speaker)} line was empty after cleanup", LlmCallCause.EmptyCompletion);
            }
            if (cleaned.Length > maxLineChars)
            {
                return Discarded(
                    $"a {DescribeSpeaker(speaker)} line ({cleaned.Length} chars) exceeded the " +
                    $"{maxLineChars}-char per-line budget — no line is ever trimmed (SPEC F127.4)", LlmCallCause.OverLength);
            }

            lines.Add(new CrosstalkAiredLine(speaker, cleaned, isInterjection));
        }

        // SPEC F139.1 (T330 review round 1 amendment): a missing HOST/NEIGHBOR turn is a malformed
        // SHAPE too — every line matched the speaker-tag format individually, but the exchange as a
        // whole never took the required two-voice shape.
        if (lines.All(line => line.Speaker != CrosstalkSpeaker.Host))
            return Discarded($"no {HostTag} line appeared — both speakers must be present", LlmCallCause.MalformedResponse);
        if (lines.All(line => line.Speaker != CrosstalkSpeaker.Neighbor))
            return Discarded($"no {NeighborTag} line appeared — both speakers must be present", LlmCallCause.MalformedResponse);

        for (var i = 1; i < lines.Count; i++)
        {
            // The sanctioned exception (SPEC F127.4): an interjection overlaps the tail of the
            // previous line rather than following it in turn order, so it is exempt from the
            // adjacent-pair alternation check below.
            if (lines[i].IsInterjection)
                continue;

            if (lines[i].Speaker == lines[i - 1].Speaker)
            {
                // SPEC F139.1 (T330 review round 1 amendment): broken alternation is a malformed
                // SHAPE — see this file's own MinLines/MaxLines check above for the amendment's
                // full rationale.
                return Discarded(
                    $"speaker alternation broken at line {i + 1} (mark an overlapping line as " +
                    $"'{InterjectionMarker}' instead)", LlmCallCause.MalformedResponse);
            }
        }

        // SPEC F138.6 (PLAN T333): the truth discard reasons — checked ONCE against the WHOLE
        // script's cleaned text (never per line): every violation discards the WHOLE exchange
        // regardless which line carried it (F127.4's own "no salvage"), so there is nothing a
        // per-line pass would buy over one scan of the joined text. Runs AFTER every shape rule
        // above has already passed — a truth check never runs against a reply that was going to be
        // discarded as malformed anyway, keeping the FIRST-rule-wins discipline this method opens
        // with intact for shape failures, with truth checked as the final gate before the duration
        // estimate.
        var scriptText = string.Join(' ', lines.Select(line => line.Text));

        // Table-driven (PLAN T333 review advisory A3): four near-identical shape checks, tried in
        // this fixed order, the first match wins. Each pairs a compiled pattern with the honest,
        // operator-facing noun phrase for its own reason line — adding a fifth shape is a one-line
        // table entry, never a fifth copy-pasted if-block.
        foreach (var (pattern, description) in TruthShapeChecks)
        {
            if (pattern.Match(scriptText) is { Success: true } match)
            {
                return Discarded(
                    $"the script named {description} (\"{match.Value}\") — " +
                    "SPEC F138.6 forbids real-world verifiables in banter", LlmCallCause.TruthGateReject);
            }
        }

        var clockResult = CopyClaims.CheckClock(scriptText, stationLocalNow);
        if (!clockResult.Passed)
        {
            var violation = clockResult.Violations[0];
            return Discarded(
                $"the script claimed \"{violation.Token}\" but the station clock reads " +
                $"{violation.Expected} — SPEC F138.6/F138.3 clock violation", LlmCallCause.TruthGateReject);
        }

        var totalChars = lines.Sum(line => line.Text.Length);
        var estimatedSeconds = totalChars / CharsPerSecond;
        if (estimatedSeconds > durationTargetSeconds)
        {
            return Discarded(
                $"estimated {estimatedSeconds:F1}s exceeds the {durationTargetSeconds}s " +
                $"{nameof(CrosstalkOptions.DurationTargetSeconds)} target", LlmCallCause.OverLength);
        }

        return new CrosstalkWriteResult.Accepted(new CrosstalkAiredScript(lines));
    }

    /// <summary>
    /// One line's speaker tag, optional interjection marker, and spoken text, split on its FIRST
    /// colon — a line with no colon, or whose pre-colon tag is neither <see cref="HostTag"/> nor
    /// <see cref="NeighborTag"/> (after stripping an optional <see cref="InterjectionMarker"/>),
    /// fails to parse. Case-insensitive against both tokens: a small model's casing is not part of
    /// the contract this parser enforces, only the STRUCTURE is.
    /// </summary>
    static bool TryParseLine(string line, out CrosstalkSpeaker speaker, out bool isInterjection, out string text)
    {
        speaker = default;
        isInterjection = false;
        text = "";

        var colonIndex = line.IndexOf(':');
        if (colonIndex <= 0)
            return false;

        var tag = line[..colonIndex].Trim();
        text = line[(colonIndex + 1)..];

        isInterjection = tag.EndsWith(InterjectionMarker, StringComparison.OrdinalIgnoreCase);
        var roleToken = (isInterjection ? tag[..^InterjectionMarker.Length] : tag).Trim();

        if (roleToken.Equals(HostTag, StringComparison.OrdinalIgnoreCase))
        {
            speaker = CrosstalkSpeaker.Host;
            return true;
        }

        if (roleToken.Equals(NeighborTag, StringComparison.OrdinalIgnoreCase))
        {
            speaker = CrosstalkSpeaker.Neighbor;
            return true;
        }

        return false;
    }

    static string DescribeSpeaker(CrosstalkSpeaker speaker) =>
        speaker == CrosstalkSpeaker.Host ? HostTag : NeighborTag;

    /// <summary>Truncates a raw model line to <see cref="MaxEchoedLineChars"/> before it is echoed
    /// into a discard reason (F127.11 review finding — see that constant's own remarks).</summary>
    static string TruncateForEcho(string text) =>
        text.Length <= MaxEchoedLineChars ? text : text[..MaxEchoedLineChars] + "…";

    static CrosstalkWriteResult.Discarded Discarded(string reason, LlmCallCause cause) => new(reason, cause);

    /// <summary>
    /// The F138.6 truth-shape table (PLAN T333 review advisory A3) — one entry per mechanical shape
    /// check, tried in this fixed order by <see cref="Parse"/>'s own truth-check loop. Built from
    /// already-compiled <see cref="Regex"/> instances (each <c>[GeneratedRegex]</c> method below
    /// returns the SAME cached singleton on every call, so building this table once at static-init
    /// costs nothing extra) rather than four separate near-identical if-blocks — adding a fifth shape
    /// is a one-line entry here, never a fifth copy-pasted branch.
    /// </summary>
    static readonly (Regex Pattern, string Description)[] TruthShapeChecks =
    [
        (FrequencyRx(), "a real-world radio frequency"),
        (CallSignRx(), "a real-world call sign"),
        // Reuses CopyClaims.ConditionWordRx directly (PLAN T333 review advisory A1) — the SAME
        // compiled pattern the F138.1 fact-block checker uses, never a byte-identical copy that
        // could silently drift the day either one changes.
        (CopyClaims.ConditionWordRx(), "a real-world weather condition"),
        (DateClaimRx(), "a real-world date"),
    ];

    // SPEC F138.6: frequency shapes.
    // ⚠️ Widened (PLAN T333 review round 1, 2026-08-20 — probe-proven F1): the original FM branch
    // required a decimal point, mirroring F138.6's own literal example ("\d+\.\d FM") too narrowly —
    // "Radio 101 FM"/"108 FM" (real FM frequencies are commonly spoken as a bare integer, decimal
    // omitted) ACCEPTED under that rule. That is the wrong direction to lean: a false PASS here airs
    // a fabricated broadcast fact (the exact harm F138.6 exists to stop), while a false DISCARD costs
    // only a silent restock (F127.4/F140 — no ladder, no template). FM now matches EITHER a decimal
    // frequency ("98.7 FM") OR a bare 2-3 digit integer one ("101 FM", "88 FM") — the FM broadcast
    // band is 88-108 MHz, always 2-3 integer digits either way, so both spellings are equally real.
    // FM carries NO clock-time collision to guard against (unlike AM below): nobody says "9 FM" to
    // mean a time of day — only "AM"/"PM" pair with clock hours in English — so the FM branch needs
    // no digit-count floor the way the AM branch does. AM instead requires a THREE-OR-FOUR-DIGIT run
    // (real AM frequencies run 540-1700 kHz, never fewer than three digits) rather than a decimal —
    // the edge this shape exists to dodge (task-pinned): "9 AM"/"12 AM" is a clock TIME, one or two
    // digits, never a station's dial position; "610 AM"/"1010 AM" is a frequency. Case-insensitive:
    // unlike ClaimVocabulary's own dropped "clear", neither "fm" nor "am" has a common innocent
    // same-shape sense worth protecting mid-sentence, and a model's casing is not part of the contract.
    [GeneratedRegex(@"\b\d+\.\d+\s?FM\b|\b\d{2,3}\s?FM\b|\b\d{3,4}\s?AM\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FrequencyRx();

    // SPEC F138.6: K/W-prefixed 3-4 letter call signs — US broadcast call signs always start with K
    // or W and are conventionally written in full caps. Deliberately CASE-SENSITIVE (no IgnoreCase):
    // an ordinary sentence-case word ("Well", "Keep", "Wednesday") never matches [A-Z]{2,3} after its
    // first letter, so case sensitivity alone is most of this shape's false-positive defense. The
    // residual gap — a genuine all-caps EXCLAMATION or INTERJECTION family that happens to start with
    // K/W ("WOW", "WHOA", "WHAT", or a K/W-initial vanity handle like "KDJ") — is accepted rather than
    // narrowed further: unlike the ordinary blurb checker's one-re-ask-to-spend posture, a crosstalk
    // false discard here costs nothing but a silent restock (F127.4/F140 — no ladder, no template,
    // the stock worker simply tries again on its own cadence).
    [GeneratedRegex(@"\b[KW][A-Z]{2,3}\b", RegexOptions.CultureInvariant)]
    private static partial Regex CallSignRx();

    // SPEC F138.6: digit-date shapes, scoped honestly to what a checker can verify mechanically —
    // a calendar-shaped year ("2026"), a month name immediately followed by a day number
    // (optionally ordinal-suffixed, "August 20"/"August 20th"), or the bare "Nth of" ordinal-date
    // shape ("the 20th of"). A BARE small number ("twenty minutes", "give me a 20") never trips any
    // of the three branches — the month/ordinal marker is required, never inferred from magnitude
    // alone, exactly the false-positive posture CopyClaims documents for its own digit-run class.
    // ClaimVocabulary.MonthAlternation is the one canonical month list (PLAN T333 review advisory
    // A2) — no second, GenWave.Tts-local copy.
    [GeneratedRegex(
        $@"\b(?:19|20)\d{{2}}\b|\b(?:{ClaimVocabulary.MonthAlternation})\s+\d{{1,2}}(?:st|nd|rd|th)?\b|\b\d{{1,2}}(?:st|nd|rd|th)\s+of\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DateClaimRx();
}
