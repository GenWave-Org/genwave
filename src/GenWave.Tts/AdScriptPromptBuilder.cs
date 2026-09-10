namespace GenWave.Tts;

using System.Text;
using GenWave.Core.Domain;

/// <summary>
/// Pure, stateless prompt composition for <see cref="AdScriptWriter"/> (SPEC F160.2, STORY-390
/// AC2/AC3) — the <c>CrosstalkPromptBuilder</c> idiom one seam over, built BESIDE it, never inside it.
///
/// <para>
/// <b>Structure-first, synthesized (Dean, 2026-09-01 — no real-copywriting-template corpus exists):</b>
/// every spot states the SAME four beats, in order — hook, pitch, tagline, call-to-action — regardless
/// of <see cref="AdScriptWriteRequest.SpotSeconds"/>; <c>GenWave.Ads.Tests</c>' own
/// <c>AFourBeatFifteenSecondStructurePasses</c> fact (PLAN T399 review F1) proves a 15s spot
/// comfortably carries all four ANNOUNCER-led lines, so only the PER-BEAT character budget scales with
/// duration, never the beat count. The stated total character budget is <see cref="CharsPerSecond"/> ×
/// <see cref="AdScriptWriteRequest.SpotSeconds"/> — deliberately no tolerance headroom added to the
/// STATED figure (the SAME "no headroom on the stated instruction" precedent
/// <c>CrosstalkPromptBuilder.BuildSystemPrompt</c>'s own remarks document for its word budget): a raw,
/// un-widened target already lands comfortably inside <c>AdScriptValidator</c>'s own duration ceiling
/// (<c>SpotSeconds × (1 + Ads:DurationToleranceRatio)</c>) with margin to spare, so nothing here needs
/// to read <see cref="AdScriptWriteRequest.ToleranceRatio"/> at all — that value earns its keep on the
/// completion's own generation cap instead (see <see cref="AdScriptWriter"/>'s own remarks).
/// </para>
///
/// <para>
/// The format contract states the SAME wire shape <c>AdScriptParser</c> (GenWave.Ads) enforces —
/// <c>TAG: line</c>, 1-3 distinct ALL-CAPS voice tags, <see cref="AnnouncerTag"/> required, the live
/// <see cref="AdScriptWriteRequest.MaxLineChars"/> per-line ceiling — duplicated as a literal string
/// here rather than referenced (this project cannot depend on GenWave.Ads, the SAME L1/L10 layering
/// reason <c>CrosstalkScriptParser.CharsPerSecond</c>'s own remarks give for duplicating a house
/// constant across a project boundary), so the model is never asked for a shape the validator would
/// reject on Format alone.
/// </para>
/// </summary>
static class AdScriptPromptBuilder
{
    /// <summary>The house spoken-rate constant (chars/second) this project already keys every other
    /// duration estimate on — reused directly (same assembly, same constant, never a third
    /// independently-tuned copy).</summary>
    const double CharsPerSecond = CrosstalkScriptParser.CharsPerSecond;

    /// <summary>The one voice tag every spot must carry (SPEC F160.3) — duplicated from
    /// <c>AdScriptParser.AnnouncerTag</c> (GenWave.Ads, unreachable from here — see this class's own
    /// remarks) rather than referenced.</summary>
    internal const string AnnouncerTag = "ANNOUNCER";

    /// <summary>The four synthesized beats every spot states, in order (this class's own remarks).</summary>
    internal static readonly string[] Beats = ["hook", "pitch", "tagline", "call-to-action"];

    /// <summary>Cap for a single field before it reaches the prompt's user content — every one of
    /// <see cref="AdScriptWriteRequest.SponsorName"/>, the brief's own <see cref="AdScriptWriteRequest.Premise"/>/
    /// <see cref="AdScriptWriteRequest.Tone"/>, and the sponsor's own <see cref="AdScriptWriteRequest.Tagline"/>/
    /// <see cref="AdScriptWriteRequest.About"/>/<see cref="AdScriptWriteRequest.Phone"/>/
    /// <see cref="AdScriptWriteRequest.Address"/>/<see cref="AdScriptWriteRequest.Website"/>/
    /// <see cref="AdScriptWriteRequest.HouseTone"/> is run through <see cref="Flatten"/>, then
    /// <c>Truncate</c>d to this cap independently — the SAME unbounded-free-text-field discipline
    /// <c>CrosstalkPromptBuilder.MaxSoulChars</c>'s own remarks document for <c>ShowName</c>/<c>Daypart</c>.</summary>
    const int MaxBriefFieldChars = 4000;

    public static string BuildSystemPrompt(AdScriptWriteRequest request)
    {
        var totalCharBudget = (int)(request.SpotSeconds * CharsPerSecond);
        var perBeatCharBudget = Math.Max(1, totalCharBudget / Beats.Length);
        var beatList = string.Join(", ", Beats);

        var scaffold =
            // gh-#696: the placeholder "TAG: <line>" was copied verbatim by the reference station's 3B
            // model (tag "TAG, quotes included). The CrosstalkPromptBuilder shape — the REAL tag names
            // inside the example — is what that writer gets away with; benched on llama3.2:3b, 24 runs:
            // this wording passes the raw format rule 79% of the time against 0-29% for the placeholder.
            $"You write a {request.SpotSeconds}-second radio ad spot script for a FICTIONAL sponsor, " +
            "in this EXACT wire format, one line per turn, nothing else before or after: " +
            $"\"{AnnouncerTag}: <line>\" for the announcer, and \"VOICE1: <line>\" or \"VOICE2: <line>\" " +
            "for up to two other voices. The word before the colon is always the VOICE speaking - " +
            $"{AnnouncerTag}, VOICE1, or VOICE2 - in capital letters with no quotes, no parentheses, and " +
            "no stage directions; never a beat name like Hook or Tagline, and never the brand name. " +
            $"{AnnouncerTag} MUST speak at least one line. " +
            $"Keep every line under {request.MaxLineChars} characters. " +
            $"Write exactly four beats in this order - {beatList} - each roughly " +
            $"{perBeatCharBudget} characters, about {totalCharBudget} characters total across the " +
            "whole spot. Never name a real brand, company, product, or trademark - invent a fictional " +
            "one instead. Any tagline, phone number, address, or website given under \"Sponsor:\" " +
            "below is a real fact you may speak verbatim - never invent facts beyond what is given " +
            "there. Any phone number spoken must use the fictional 555 exchange, for example " +
            "555-0142, unless the sponsor's real phone number is given under \"Sponsor:\" below, in " +
            "which case speak that one instead. No stage directions, no emoji, no markdown formatting.";

        var postureLine = request.Posture == AudiencePosture.Everyone
            ? " Keep the language family-friendly."
            : "";

        return scaffold + postureLine;
    }

    /// <summary>
    /// Builds the user content the model sees (SPEC F174.8, STORY-428): one "Sponsor:" line naming the
    /// sponsor, then one line per PRESENT sponsor fact (<see cref="AdScriptWriteRequest.Tagline"/>/
    /// <see cref="AdScriptWriteRequest.About"/>/<see cref="AdScriptWriteRequest.Phone"/>/
    /// <see cref="AdScriptWriteRequest.Address"/>/<see cref="AdScriptWriteRequest.Website"/> — STORY-428
    /// AC3 (a null fact is absent); a whitespace-only fact is absent by T443 ruling), then
    /// <see cref="AdScriptWriteRequest.Premise"/> if present, then exactly one "Tone:" line: the
    /// brief's own <see cref="AdScriptWriteRequest.Tone"/> when present, else
    /// <see cref="AdScriptWriteRequest.HouseTone"/> when present, else no "Tone:" line at all (AC4,
    /// PLAN T443 ruling), then the fixed closing instruction. Each label appears at most once — every
    /// value emitted below is run through <see cref="Flatten"/> first (T443 ruling), so a fact's own
    /// text can never smuggle a newline-delimited "Label:" line of its own into this content and forge
    /// or duplicate one.
    /// </summary>
    public static string BuildUserContent(AdScriptWriteRequest request)
    {
        var lines = new List<string> { $"Sponsor: {Truncate(Flatten(request.SponsorName), MaxBriefFieldChars)}" };

        AddFactLine(lines, "Tagline", request.Tagline);
        AddFactLine(lines, "About", request.About);
        AddFactLine(lines, "Phone", request.Phone);
        AddFactLine(lines, "Address", request.Address);
        AddFactLine(lines, "Website", request.Website);

        var premise = Flatten(request.Premise);
        if (premise.Length > 0)
            lines.Add($"Premise: {Truncate(premise, MaxBriefFieldChars)}");

        var briefTone = Flatten(request.Tone);
        var houseTone = Flatten(request.HouseTone);
        var tone = briefTone.Length > 0 ? briefTone : houseTone.Length > 0 ? houseTone : null;
        if (tone is not null)
            lines.Add($"Tone: {Truncate(tone, MaxBriefFieldChars)}");

        lines.Add("Write the spot now.");
        return string.Join('\n', lines);
    }

    /// <summary>Appends one "{label}: {value}" line when <paramref name="value"/> carries a fact once
    /// <see cref="Flatten"/>ed — a <see langword="null"/>, blank, or whitespace-only
    /// <paramref name="value"/> all flatten to <see cref="string.Empty"/> and get no line, the SAME
    /// "nothing to show" posture either way (STORY-428 AC3; T443 ruling).</summary>
    static void AddFactLine(List<string> lines, string label, string? value)
    {
        var flat = Flatten(value);
        if (flat.Length > 0)
            lines.Add($"{label}: {Truncate(flat, MaxBriefFieldChars)}");
    }

    /// <summary>
    /// Neutralizes a value before it can reach the user content as a line of its own (T443 ruling): a
    /// sponsor's own free-text fact — <see cref="AdScriptWriteRequest.About"/> above all, the one field
    /// with real room for it — reaches this prompt via <c>POST</c>/<c>PATCH /api/sponsors</c> with only
    /// its ends trimmed, so an embedded newline could otherwise open its own synthetic
    /// "<c>Sponsor:</c>"/"<c>Tone:</c>"/"<c>Phone:</c>" line and forge a fact the sponsor never gave, or
    /// duplicate a label <see cref="BuildUserContent"/> already emits once. Every
    /// <see cref="char.IsControl(char)"/> code point (a newline chief among them) becomes a space, then
    /// runs of whitespace — original spaces/tabs and control-character replacements alike — collapse to
    /// one space and the result is trimmed (<see cref="SpeechText.CollapseWhitespace"/>, the SAME rule
    /// this assembly already applies after every other text-rewriting pass), so the line is the
    /// delimiter and a flattened value can never contain one. A <see langword="null"/> value flattens to
    /// <see cref="string.Empty"/> — the SAME "absent" shape a blank or whitespace-only value produces,
    /// so "present" has exactly one definition for every field this class emits (STORY-428 AC3).
    /// </summary>
    static string Flatten(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var neutralized = new StringBuilder(value.Length);
        foreach (var c in value)
            neutralized.Append(char.IsControl(c) ? ' ' : c);

        return SpeechText.CollapseWhitespace(neutralized.ToString());
    }

    /// <summary>
    /// SPEC F160.3's ladder shape — the ONE re-ask line, appended to the SAME user prompt the rejected
    /// draft already saw (never a prompt rebuilt from scratch, the <c>LlmPromptBuilder.BuildTruthGateReaskLine</c>
    /// precedent one project over), naming the violated rule and the validator's own reason so the
    /// retry has something concrete to fix rather than a bare "try again".
    /// </summary>
    public static string BuildReaskLine(string ruleId, string reason) =>
        $"Your last draft violated the '{ruleId}' rule: {reason}. Write a new draft that fixes this " +
        "and obeys every other instruction above.";

    static string Truncate(string text, int maxChars) => text.Length <= maxChars ? text : text[..maxChars];
}
