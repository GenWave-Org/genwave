namespace GenWave.Tts;

using System.Globalization;
using GenWave.Core.Domain;

/// <summary>
/// Pure, stateless prompt composition for <see cref="LlmCopyWriter"/> (SPEC F34.3, F35.2, F35.3,
/// F71.3, F71.8) — split out on its own (T37, STORY-193) so the writer's HTTP/single-flight/hygiene
/// concerns don't keep growing around an unrelated "how do we phrase this prompt" one. Every member
/// here is a free function of its arguments; nothing touches <see cref="LlmCopyWriter"/>'s own
/// instance state (the single-flight gate, the options monitor, the logger), which is exactly why
/// none of it needs to live on that class.
/// </summary>
static class LlmPromptBuilder
{
    // T6 reviewer follow-up (T4): Backstory/Style/Soul are unbounded operator-entered text (F35.1
    // has no length cap on the persona row) that flows straight into an LLM prompt. Capped rather
    // than left open so one oversized persona can't balloon every render's request body / token
    // spend; a few thousand chars is generous for soul/backstory/style prose while still bounding
    // it. See BuildSoul's own remarks for exactly where this applies (whole-string for a card's
    // Soul, per-field for the legacy Backstory/Style fallback).
    const int MaxSoulChars = 4000;

    // F71.3 (STORY-193): "2-3 per generation, never all" — see SampleQuirks' own remarks.
    const int MinSampledQuirks = 2;
    const int MaxSampledQuirks = 3;

    /// <summary>
    /// Baked house scaffold for the system prompt (SPEC F34.3): personality-neutral radio DJ, 1-2
    /// spoken sentences, no stage directions. <paramref name="personaSection"/> (SPEC F35.2, F35.3,
    /// F71.3) appends an active persona's soul + sampled quirks beneath the scaffold; null/empty
    /// (no active persona, or one with nothing to show) leaves the neutral scaffold untouched —
    /// blurbs work persona-less exactly as before T6.
    /// </summary>
    public static string BuildSystemPrompt(string? personaSection)
    {
        const string Scaffold =
            "You are a personality-neutral radio DJ writing live station patter. Write exactly one " +
            "or two sentences of spoken copy to be read aloud on air. Plain spoken words only - no " +
            "stage directions, no emoji, no markdown formatting, no sound-effect cues. You may " +
            "embellish with genuine knowledge of the track, artist, or era.";

        return string.IsNullOrEmpty(personaSection) ? Scaffold : $"{Scaffold}\n\n{personaSection}";
    }

    /// <summary>
    /// Composes the persona section (SPEC F35.2, F35.3, F71.3): a soul line/block (see
    /// <see cref="BuildSoul"/>) plus, when the card carries any, a line of 2-3 SAMPLED quirks (see
    /// <see cref="SampleQuirks"/>) — never the full set (F71.3). A persona that yields neither
    /// (no soul text, no quirks) returns null (falls back to the neutral scaffold — the "neutral
    /// otherwise" half of F35.2, not just the no-persona case).
    /// </summary>
    public static string? BuildPersonaSection(Persona? persona, PersonaCard? card)
    {
        var lines = new List<string>();

        var soul = BuildSoul(persona, card);
        if (!string.IsNullOrEmpty(soul))
            lines.Add(soul);

        if (card is { Quirks.Count: > 0 })
        {
            var sampled = SampleQuirks(card.Quirks);
            if (sampled.Count > 0)
                lines.Add($"Quirks: {string.Join("; ", sampled)}");
        }

        return lines.Count == 0 ? null : string.Join('\n', lines);
    }

    /// <summary>
    /// Soul read-path decision (T36 review carry-forward #2, STORY-193): prefer the ACTIVE
    /// persona's <see cref="PersonaCard.Soul"/> when it has any content, falling back to the legacy
    /// Backstory/Style composition (<see cref="BuildLegacySoul"/> below) only when there is no card,
    /// or the card's Soul is empty (a not-yet-migrated or otherwise anomalous row). A persona that
    /// predates the card schema, or a preview auditioning an explicit override with no card (see
    /// <see cref="LlmCopyWriter.WritePreviewAsync"/>), keeps working exactly as it did before F71.
    ///
    /// For an admin-managed persona this card.Soul is byte-identical to <see cref="BuildLegacySoul"/>
    /// of that SAME persona's own Backstory/Style (<c>LegacyPersonaCardMapper.BuildSoul</c> mirrors
    /// it on purpose, and <c>PersonaRepository.UpdateAsync</c> keeps both in lockstep on every write)
    /// — but this is NOT a universal guarantee. <c>PersonaCardMigrator</c>'s dedicated
    /// <c>"default"</c> bootstrap row is the documented exception: its card.Soul is a ONE-TIME
    /// SNAPSHOT of whichever persona was active at migration time, while its own legacy
    /// Backstory/Style columns are left at their empty defaults (that insert never populates them)
    /// — so for that row specifically, card.Soul and <c>BuildLegacySoul(thatPersona)</c> diverge by
    /// design, and this branch is exactly what keeps the snapshot text from being silently dropped.
    ///
    /// <see cref="MaxSoulChars"/> is applied once, to the whole composed string, rather than
    /// per-field the way the legacy branch still does (T36 carry-forward #1: "preserve the
    /// truncation semantics... apply the same cap"). This is byte-identical to the pre-F71 output
    /// for the overwhelming common case — any persona whose backstory+style combined stays under
    /// 4000 chars, which is virtually all of them — and only diverges for the rare persona whose
    /// two fields combined tip past that ceiling, a documented, accepted trade-off rather than
    /// carrying two separate 4000-char budgets forward into a single already-concatenated string.
    /// </summary>
    static string BuildSoul(Persona? persona, PersonaCard? card)
    {
        if (card is { Soul.Length: > 0 })
            return Truncate(card.Soul, MaxSoulChars);

        return persona is null ? "" : BuildLegacySoul(persona);
    }

    /// <summary>
    /// The exact pre-F71 composition (SPEC F35.2, F35.3): one labeled line per non-empty
    /// <see cref="Persona.Backstory"/>/<see cref="Persona.Style"/> field, each independently capped
    /// at <see cref="MaxSoulChars"/>, empty fields skipped entirely.
    /// </summary>
    static string BuildLegacySoul(Persona persona)
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(persona.Backstory))
            lines.Add($"Backstory: {Truncate(persona.Backstory, MaxSoulChars)}");
        if (!string.IsNullOrEmpty(persona.Style))
            lines.Add($"Style: {Truncate(persona.Style, MaxSoulChars)}");

        return lines.Count == 0 ? "" : string.Join('\n', lines);
    }

    static string Truncate(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..maxChars];

    /// <summary>
    /// Samples 2-3 quirks from <paramref name="quirks"/>, never the full set once there are more
    /// than <see cref="MaxSampledQuirks"/> to choose from (SPEC F71.3, pinned by test: a
    /// five-quirk persona never sees all five in one prompt). Fewer than
    /// <see cref="MaxSampledQuirks"/>+1 quirks on the card means there is nothing left to trim —
    /// every one of them ships as-is. <see cref="Random.Shared"/> (thread-safe, no seed needed —
    /// tests assert the 2-3 bound across many generations rather than an exact sequence); the
    /// SELECTED subset is re-sorted back to the card's own original order before it reaches the
    /// prompt, so the SET is random but the ORDER within any one prompt is always deterministic.
    /// </summary>
    static IReadOnlyList<string> SampleQuirks(IReadOnlyList<string> quirks)
    {
        if (quirks.Count <= MaxSampledQuirks)
            return quirks;

        var sampleSize = Random.Shared.Next(MinSampledQuirks, MaxSampledQuirks + 1);
        var indices = Enumerable.Range(0, quirks.Count).ToArray();
        Random.Shared.Shuffle(indices);
        return indices.Take(sampleSize).Order().Select(i => quirks[i]).ToList();
    }

    /// <summary>
    /// The DJ's clock (SPEC F71.8, gh-#13): every LLM prompt this writer builds — persona active or
    /// not — states the current date, weekday, and time in station-local terms, so the model
    /// answers from the injected clock rather than inventing one. "Station-local" is
    /// <paramref name="timeProvider"/>'s own <see cref="TimeProvider.LocalTimeZone"/> — in
    /// production this is <see cref="TimeProvider.System"/>, i.e. the container's own TZ (no
    /// dedicated <c>Station:Timezone</c> setting exists; the container's configured timezone IS
    /// "station-local" today).
    ///
    /// Formatted with <see cref="CultureInfo.InvariantCulture"/> (review finding, T37): this line is
    /// LLM-facing wire content, not UI display text — a host running under a non-English
    /// <c>CurrentCulture</c> (e.g. de-DE) would otherwise emit localized weekday/month names
    /// ("Montag", "vorm.") or even a non-Gregorian calendar year (th-TH's Buddhist calendar), neither
    /// of which the prompt's English scaffold (<see cref="BuildSystemPrompt"/>) or the model's
    /// English house style expects.
    /// </summary>
    public static string BuildStationClockLine(TimeProvider timeProvider)
    {
        var stationLocalNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeProvider.LocalTimeZone);
        return
            $"Current date/time (station-local): {stationLocalNow.ToString("dddd, MMMM d, yyyy, h:mm tt", CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// SPEC F83.2 — the exploration lampshade: the pick came from the bias-blind exploration slice
    /// (<c>PersonaRanker.PickAsync</c>'s own contract guarantees <see cref="PersonaPickDiagnostics.FiredRules"/>
    /// is empty in this case — bias-blind by construction, never a post-hoc zeroing), so there is
    /// nothing to attribute it to. States plainly that the pick sits outside the persona's usual
    /// taste and invites an OPTIONAL lampshade ("not my usual...") — never a fired rule.
    /// </summary>
    const string ExplorationLampshadeLine =
        "Taste note: this pick sits outside the persona's usual taste (an exploration pick) - you " +
        "may lampshade that on air (e.g. \"not my usual pick, but...\"); never credit it to a taste rule.";

    /// <summary>
    /// SPEC F87.7 (STORY-228, PLAN T91) — the request-color instruction line for a lead-in whose
    /// track was pulled onto air by the fulfillment rung (<see cref="MediaItem.RequestFulfilled"/>,
    /// carried straight from <c>GenWave.Orchestration.Orchestrator</c>'s own carry-through, PLAN T90).
    /// A CONSTANT instruction, never interpolation — there is no wish text, no parsed predicate, and
    /// no listener-supplied fragment anywhere in <see cref="SegmentRequest"/> or <see cref="MediaItem"/>
    /// for this line (or anything else in this prompt) to interpolate from; see
    /// <c>GenWave.Tts.Tests.Specs.Story228_RequestShoutOut</c>'s reflection fact for the structural
    /// proof. Deliberately vague about who/why: the copy MAY acknowledge the request line, but must
    /// never invent or imply a specific listener, message, or reason — the model was never shown one.
    /// </summary>
    const string RequestLineAcknowledgmentLine =
        "Request note: this track came in from the station's request line - mention that on air (e.g. " +
        "\"got this one in from the request line\"); never say who requested it or why, and never " +
        "repeat any listener wording - you were never shown any.";

    /// <summary>
    /// Composes the user-content half of the prompt (SPEC F34.3, F71.8, F83.1-F83.3, F87.7): station/
    /// time/clock/segment framing, then whatever the track itself carries (title/artist/album/genre/
    /// year — unchanged since before F71), then an OPTIONAL request-color line (SPEC F87.7, PLAN T91 —
    /// see <see cref="RequestLineAcknowledgmentLine"/>) for a fulfilled track's own lead-in only, then,
    /// last, an OPTIONAL persona-taste line (see <see cref="BuildTasteLine"/>) so each reads as one
    /// more piece of color about THIS track rather than a separate directive.
    /// <paramref name="previouslyVoicedTasteNotes"/> is the immediately preceding ON-AIR break's
    /// fired-rule descriptions (see <see cref="DescribeFiredRules"/>) — see <see cref="LlmCopyWriter"/>'s
    /// own remarks on where that memory lives and why a preview never supplies it.
    /// </summary>
    public static string BuildUserContent(
        SegmentRequest request, string stationClockLine, IReadOnlyList<string> previouslyVoicedTasteNotes)
    {
        var lines = new List<string>
        {
            $"Station: {request.StationName}",
            $"Local time: {request.LocalNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}",
            stationClockLine,
            request.Kind == SegmentKind.LeadIn
                ? "Segment: lead-in for the upcoming track."
                : "Segment: back-announce for the track that just played.",
        };

        if (request.Track is { } track)
        {
            lines.Add($"Title: {track.Title}");
            if (!string.IsNullOrEmpty(track.Artist)) lines.Add($"Artist: {track.Artist}");
            if (!string.IsNullOrEmpty(track.Album)) lines.Add($"Album: {track.Album}");
            if (!string.IsNullOrEmpty(track.Genre)) lines.Add($"Genre: {track.Genre}");
            if (track.Year is { } year) lines.Add($"Year: {year}");

            // SPEC F87.7: only a fulfilled track's OWN lead-in carries request color — never its
            // back-announce (a fulfilled track that already aired still carries RequestFulfilled on
            // MediaItem, so Kind gates this, not just the flag).
            if (request.Kind == SegmentKind.LeadIn && track.RequestFulfilled)
                lines.Add(RequestLineAcknowledgmentLine);

            var tasteLine = BuildTasteLine(track.PersonaPick, previouslyVoicedTasteNotes);
            if (tasteLine is not null)
                lines.Add(tasteLine);
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// SPEC F83.1, F83.2, F83.3 (STORY-214, PLAN T65) — the persona-taste line, or null when there is
    /// nothing to say. Three shapes, in priority order:
    ///
    /// <list type="bullet">
    /// <item>
    /// <paramref name="personaPick"/> is null (F83.3) — no persona pick backed this track (persona
    /// layer off, or any envelope-only ladder pick with no ranker involved, SPEC F81.6) — returns
    /// null unconditionally, even when <paramref name="previouslyVoicedTasteNotes"/> is non-empty
    /// (a stale marker from an earlier persona-on break must never leak into a persona-off prompt).
    /// Nothing is appended, so a persona-off prompt is byte-identical to the pre-F82 shape — the
    /// regression pin.
    /// </item>
    /// <item>
    /// <see cref="PersonaPickDiagnostics.IsExploration"/> (F83.2) returns
    /// <see cref="ExplorationLampshadeLine"/> — <see cref="PersonaPickDiagnostics.FiredRules"/> is
    /// empty by construction for an exploration pick, so this branch never has a rule to attribute
    /// the pick to either way.
    /// </item>
    /// <item>
    /// One or more <see cref="PersonaPickDiagnostics.FiredRules"/> (F83.1) — phrased as OPTIONAL
    /// color ("may mention"), never a mandate: the persona's own taste rules are a hint the copy MAY
    /// use, not a script it has to read. When this break's fired-rule descriptions overlap
    /// <paramref name="previouslyVoicedTasteNotes"/> at all, an extra sentence asks for different
    /// phrasing (or silence) — the anti-repetition posture — rather than let the same color line
    /// repeat break after break.
    /// </item>
    /// </list>
    /// </summary>
    static string? BuildTasteLine(PersonaPickDiagnostics? personaPick, IReadOnlyList<string> previouslyVoicedTasteNotes)
    {
        if (personaPick is null)
            return null;

        if (personaPick.IsExploration)
            return ExplorationLampshadeLine;

        if (personaPick.FiredRules.Count == 0)
            return null;

        var notes = DescribeFiredRules(personaPick.FiredRules);
        var summary = string.Join("; ", notes);
        var recentlyVoiced = notes.Any(previouslyVoicedTasteNotes.Contains);

        var line =
            $"Taste note: this pick matches the persona's taste for {summary} - you may mention this " +
            "if it fits naturally; it's color, not a requirement.";

        return recentlyVoiced
            ? line + " You voiced this same taste note on the last break too - vary the phrasing, or leave it out this time."
            : line;
    }

    /// <summary>
    /// One short, spoken-friendly phrase per fired <see cref="TasteRule"/> (artist over genre over
    /// tag — mirrors <c>Orchestrator.FormatFiredRule</c>'s own precedence for its debug line, worded
    /// here for a human ear rather than a log grep). Exposed (not <see langword="private"/>) so
    /// <see cref="LlmCopyWriter"/> can compute the SAME descriptions for the break it just rendered
    /// and remember them as next break's <c>previouslyVoicedTasteNotes</c> — one description function,
    /// used on both the "what did we just say" and "what are we about to say" sides of the
    /// anti-repetition comparison.
    /// </summary>
    public static IReadOnlyList<string> DescribeFiredRules(IReadOnlyList<TasteRule> firedRules) =>
        firedRules.Select(DescribeFiredRule).ToList();

    static string DescribeFiredRule(TasteRule rule) =>
        rule.Predicate.LabelOr("this pick");
}
