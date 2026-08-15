namespace GenWave.Tts;

/// <summary>
/// Strict parse + validation for a <see cref="CrosstalkScriptWriter"/> completion reply (SPEC F127.3,
/// F127.4, STORY-326 AC2, AC3, AC4, AC6). Fail-closed by construction: the FIRST rule a reply breaks
/// is the one returned — no partial credit, no salvage, no template rung (F127.4's "the failure mode
/// is skip"). <see cref="HostTag"/>/<see cref="NeighborTag"/>/<see cref="InterjectionMarker"/> are the
/// single source of truth for the wire format — <see cref="CrosstalkPromptBuilder"/> states the exact
/// same three tokens in the instructions it builds, so the model is never asked to emit a shape this
/// parser doesn't also accept.
/// </summary>
static class CrosstalkScriptParser
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
    /// Parses and fully validates one completion reply into a <see cref="CrosstalkScript"/> (SPEC
    /// F127.3, F127.4). <paramref name="maxLineChars"/> is the per-line char budget (the SAME
    /// <c>Llm:MaxCopyChars</c> ceiling an ordinary blurb carries — no second setting); a line over it
    /// discards the WHOLE exchange, never a trim (F127.4). <paramref name="durationTargetSeconds"/> is
    /// the live <see cref="CrosstalkOptions.DurationTargetSeconds"/> value.
    /// </summary>
    public static CrosstalkWriteResult Parse(string rawResponse, int maxLineChars, int durationTargetSeconds)
    {
        var rawLines = rawResponse
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        if (rawLines.Count is < MinLines or > MaxLines)
        {
            return Discarded(
                $"expected {MinLines}-{MaxLines} speaker-tagged lines, got {rawLines.Count}");
        }

        var lines = new List<CrosstalkLine>(rawLines.Count);
        foreach (var rawLine in rawLines)
        {
            if (!TryParseLine(rawLine, out var speaker, out var isInterjection, out var rawText))
            {
                return Discarded(
                    $"line does not match the '{HostTag}:'/'{NeighborTag}:' speaker-tag format: " +
                    $"\"{TruncateForEcho(rawLine)}\"");
            }

            var cleaned = LlmCopyWriter.ApplyCopyHygiene(rawText);
            if (cleaned.Length == 0)
                return Discarded($"a {DescribeSpeaker(speaker)} line was empty after cleanup");
            if (cleaned.Length > maxLineChars)
            {
                return Discarded(
                    $"a {DescribeSpeaker(speaker)} line ({cleaned.Length} chars) exceeded the " +
                    $"{maxLineChars}-char per-line budget — no line is ever trimmed (SPEC F127.4)");
            }

            lines.Add(new CrosstalkLine(speaker, cleaned, isInterjection));
        }

        if (lines.All(line => line.Speaker != CrosstalkSpeaker.Host))
            return Discarded($"no {HostTag} line appeared — both speakers must be present");
        if (lines.All(line => line.Speaker != CrosstalkSpeaker.Neighbor))
            return Discarded($"no {NeighborTag} line appeared — both speakers must be present");

        for (var i = 1; i < lines.Count; i++)
        {
            // The sanctioned exception (SPEC F127.4): an interjection overlaps the tail of the
            // previous line rather than following it in turn order, so it is exempt from the
            // adjacent-pair alternation check below.
            if (lines[i].IsInterjection)
                continue;

            if (lines[i].Speaker == lines[i - 1].Speaker)
            {
                return Discarded(
                    $"speaker alternation broken at line {i + 1} (mark an overlapping line as " +
                    $"'{InterjectionMarker}' instead)");
            }
        }

        var totalChars = lines.Sum(line => line.Text.Length);
        var estimatedSeconds = totalChars / CharsPerSecond;
        if (estimatedSeconds > durationTargetSeconds)
        {
            return Discarded(
                $"estimated {estimatedSeconds:F1}s exceeds the {durationTargetSeconds}s " +
                $"{nameof(CrosstalkOptions.DurationTargetSeconds)} target");
        }

        return new CrosstalkWriteResult.Accepted(new CrosstalkScript(lines));
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

    static CrosstalkWriteResult.Discarded Discarded(string reason) => new(reason);
}
