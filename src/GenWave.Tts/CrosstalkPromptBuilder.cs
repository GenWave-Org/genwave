namespace GenWave.Tts;

using GenWave.Core.Domain;

/// <summary>
/// Pure, stateless prompt composition for <see cref="CrosstalkScriptWriter"/> (SPEC F127.3, STORY-326)
/// — built BESIDE <see cref="LlmPromptBuilder"/>, never inside it (F116's own byte-identical
/// discipline for the ordinary break prompt: that prompt must never change at all, and there are
/// golden byte-pins proving it). The two builders share only what is already a small, general-purpose
/// helper (<see cref="LlmPromptBuilder.BuildPersonaSection"/> for each card's soul/quirks,
/// <see cref="LlmPromptBuilder.BuildStationClockLine"/> for the station-local clock line) — nothing
/// here reaches INTO an ordinary-break prompt method, and nothing there reaches into this one.
/// </summary>
static class CrosstalkPromptBuilder
{
    /// <summary>
    /// Chars-per-word divisor for the stated word-budget instruction (mirrors
    /// <c>LlmPromptBuilder</c>'s own private divisor of the identical value, for the identical
    /// reason — see <see cref="LlmCopyWriter.CharsPerTokenDivisor"/>'s neighboring remarks on why a
    /// WORD estimate divides by the true average rather than padding for headroom the way a TOKEN
    /// cap does). STATED only, exactly like the ordinary-break prompt's own word figure — the
    /// completion request's <c>max_tokens</c> (<see cref="LlmCopyWriter.DeriveMaxTokens"/>) is what
    /// actually bounds generation.
    /// </summary>
    const int CharsPerWordDivisor = 6;

    /// <summary>
    /// Cap for <see cref="CrosstalkExchangeRequest.ShowName"/>/<see cref="CrosstalkExchangeRequest.Daypart"/>
    /// before either reaches the prompt (T282 review finding, SPEC F127.3/F127.11): both are
    /// operator-editable hooks with no length constraint of their own (the db show name column is
    /// unbounded text) flowing straight into a prompt, exactly the class of field every
    /// <c>LlmPromptBuilder</c> counterpart truncates first (see that class's own
    /// <c>BuildShowLine</c>/<c>BuildHandoffLine</c> remarks). Mirrors <c>LlmPromptBuilder</c>'s own
    /// private <c>MaxSoulChars</c> value for the identical reason — duplicated, not referenced,
    /// exactly like <see cref="CharsPerWordDivisor"/> above (see that constant's own remarks for why
    /// this file states its own copy rather than reaching into a private member one class over).
    /// </summary>
    const int MaxSoulChars = 4000;

    /// <summary>
    /// The banter scaffold plus both speakers' persona sections (SPEC F127.3). Deliberately never
    /// mentions a track, a song, or "what's playing" — this writer's caller (a LATER task's
    /// <c>CrosstalkPlanner</c>) never hands one in (see <see cref="CrosstalkExchangeRequest"/>'s own
    /// remarks), so there is nothing here for the model to be asked about.
    /// </summary>
    public static string BuildSystemPrompt(PersonaCard hostCard, PersonaCard neighborCard, int maxCopyChars)
    {
        var wordBudget = Math.Max(1, maxCopyChars / CharsPerWordDivisor);

        var scaffold =
            $"You write a short, natural-sounding radio banter exchange between two DJs sharing " +
            $"the booth: {CrosstalkScriptParser.HostTag} (the on-air host) and " +
            $"{CrosstalkScriptParser.NeighborTag} (a DJ dropping in). " +
            $"Write between {CrosstalkScriptParser.MinLines} and {CrosstalkScriptParser.MaxLines} " +
            "short lines total, alternating speakers, in this EXACT format, one line per turn, " +
            $"nothing else before or after: \"{CrosstalkScriptParser.HostTag}: <line>\" or " +
            $"\"{CrosstalkScriptParser.NeighborTag}: <line>\". " +
            $"Across the WHOLE exchange use no more than approximately {wordBudget} words total. " +
            "Both DJs must speak at least once. Keep each line short and conversational, no commas, " +
            "no stage directions, no emoji, no markdown formatting. " +
            $"To have one speaker briefly cut in over the other's line, tag that one line " +
            $"\"{CrosstalkScriptParser.HostTag} {CrosstalkScriptParser.InterjectionMarker}: <line>\" " +
            $"or \"{CrosstalkScriptParser.NeighborTag} {CrosstalkScriptParser.InterjectionMarker}: " +
            "<line>\" instead of alternating — use this rarely, only for a genuine interruption. " +
            "Never mention a specific song, artist, or track - neither DJ knows what is playing " +
            "right now.";

        var hostSection = LlmPromptBuilder.BuildPersonaSection(persona: null, hostCard);
        var neighborSection = LlmPromptBuilder.BuildPersonaSection(persona: null, neighborCard);

        var lines = new List<string> { scaffold };
        lines.Add($"{CrosstalkScriptParser.HostTag} persona:\n{hostSection ?? "(no defined persona)"}");
        lines.Add($"{CrosstalkScriptParser.NeighborTag} persona:\n{neighborSection ?? "(no defined persona)"}");

        return string.Join("\n\n", lines);
    }

    /// <summary>
    /// Station/show/daypart/time-of-day hooks (SPEC F127.3) — every line here is OPTIONAL and omitted
    /// entirely when the caller has nothing to say, mirroring <c>LlmPromptBuilder.BuildShowLine</c>'s
    /// own "invent nothing beyond what's given" discipline one seam over. No track line exists because
    /// <see cref="CrosstalkExchangeRequest"/> carries no track to build one from.
    /// </summary>
    public static string BuildUserContent(CrosstalkExchangeRequest request, string stationClockLine)
    {
        var lines = new List<string>
        {
            $"Station: {request.StationName}",
            stationClockLine,
        };

        if (request.ShowName is { Length: > 0 } showName)
            lines.Add($"Show: {Truncate(showName, MaxSoulChars)}");
        if (request.Daypart is { Length: > 0 } daypart)
            lines.Add($"Time of day: {Truncate(daypart, MaxSoulChars)}");

        lines.Add("Write the exchange now.");

        return string.Join('\n', lines);
    }

    /// <summary>Mirrors <c>LlmPromptBuilder</c>'s own private <c>Truncate</c> helper for the
    /// identical reason <see cref="MaxSoulChars"/>'s own remarks give.</summary>
    static string Truncate(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..maxChars];
}
