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
    /// completion request's <c>max_tokens</c> (<see cref="CrosstalkScriptWriter"/>'s own
    /// duration-derived cap) is what actually bounds generation, and carries its OWN headroom —
    /// this divisor never does.
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
    /// SPEC F138.6's narrow anti-fabrication clause — <see cref="LlmPromptBuilder.BuildClockGuardLine"/>'s
    /// sibling one project seam over, joining the banter scaffold beside its other style rules (never a
    /// separate paragraph, exactly like <see cref="LlmPromptBuilder.BuildSystemPrompt"/>'s own F138.5
    /// guard line one method over). Forbids exactly the shapes <see cref="CrosstalkScriptParser.Parse"/>'s
    /// own F138.6 mechanical checks look for (a real frequency, call sign, place name, weather
    /// condition, or date) and explicitly ALLOWS the opposite — invented lore is good radio, not a
    /// violation, so the prompt says so directly rather than leaving a model to guess whether "the
    /// legendary DJ Ghost of the Graveyard Shift" is forbidden fabrication or welcome color. Real-place
    /// invention is prompt-only by design (SPEC F138.6): a checker cannot tell a real city from an
    /// invented one, so this clause is the ONLY place that half of the rule is ever enforced at all.
    /// Comma-free (gh-#303 style lesson, <see cref="LlmPromptBuilder.BuildClockGuardLine"/>'s own
    /// precedent) — one short sentence per forbidden shape rather than a comma-joined list, so the
    /// prompt asking the model to avoid fabricated specifics does not itself read like one.
    /// </summary>
    const string AntiFabricationClause =
        "Never mention a real radio frequency. Never mention a real call sign. Never mention a real " +
        "place name. Never mention a real weather condition. Never mention a real date. Invented " +
        "recurring characters running gags and station mythology are welcome.";

    /// <summary>
    /// The banter scaffold plus both speakers' persona sections (SPEC F127.3). Deliberately never
    /// mentions a track, a song, or "what's playing" — this writer's caller (a LATER task's
    /// <c>CrosstalkPlanner</c>) never hands one in (see <see cref="CrosstalkExchangeRequest"/>'s own
    /// remarks), so there is nothing here for the model to be asked about.
    ///
    /// <para>
    /// <paramref name="durationTargetSeconds"/> is the live <see cref="CrosstalkOptions.DurationTargetSeconds"/>
    /// value (SPEC F127.3, T283 paper-audition reconciliation, gh-#385) — the stated word budget asks
    /// for what <see cref="CrosstalkScriptParser.Parse"/>'s own duration gate will actually accept,
    /// not a figure scaled off <see cref="LlmOptions.MaxCopyChars"/> (the per-LINE budget, which has
    /// no fixed relationship to how many lines a script carries).
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Amended (PLAN T333 review round 1, 2026-08-20 — probe-proven F2):</b> <paramref name="stationLocalNow"/>
    /// appends <see cref="LlmPromptBuilder.BuildClockGuardLine"/> beside <see cref="AntiFabricationClause"/>
    /// — the SAME F138.5 guard line every other patter prompt already carries. Before this amendment,
    /// crosstalk was the only patter kind whose F138.3 clock check ran with NO prompt-side rule at
    /// all: the model was told the clock as a fact (the station clock line in the user content) but
    /// never instructed not to contradict it, then silently discarded for a wrong-day claim with no
    /// re-ask and no template to fall back on (F127.4) — pure F140-class generation waste on the
    /// fenced ollama for a mistake the prompt itself never warned against. <paramref name="stationLocalNow"/>
    /// is the SAME generation-time instant <see cref="CrosstalkScriptWriter.WriteExchangeAsync"/>
    /// threads into both this guard line and <see cref="CrosstalkScriptParser.Parse"/>'s own clock
    /// check (<see cref="CrosstalkExchangeRequest.StationLocalNow"/>) — one shared instant, never two
    /// separately-computed ones.
    /// </para>
    /// </summary>
    public static string BuildSystemPrompt(
        PersonaCard hostCard, PersonaCard neighborCard, int durationTargetSeconds, DateTimeOffset stationLocalNow)
    {
        // Same spoken-rate estimate CrosstalkScriptParser.Parse applies to an accepted script — the
        // STATED word budget asks for what the duration gate will accept, no headroom added (unlike
        // the completion request's own max_tokens cap; see CrosstalkScriptWriter.DeriveScriptGenerationCap
        // for why that cap carries headroom and this instruction deliberately does not).
        var estimatedChars = durationTargetSeconds * CrosstalkScriptParser.CharsPerSecond;
        var wordBudget = Math.Max(1, (int)estimatedChars / CharsPerWordDivisor);

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
            AntiFabricationClause + " " + LlmPromptBuilder.BuildClockGuardLine(stationLocalNow) + " " +
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
