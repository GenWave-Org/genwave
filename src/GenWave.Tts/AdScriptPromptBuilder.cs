namespace GenWave.Tts;

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
    const string AnnouncerTag = "ANNOUNCER";

    /// <summary>The four synthesized beats every spot states, in order (this class's own remarks).</summary>
    static readonly string[] Beats = ["hook", "pitch", "tagline", "call-to-action"];

    /// <summary>Cap for an operator-authored brief field (<see cref="AdScriptWriteRequest.Brand"/>/
    /// <see cref="AdScriptWriteRequest.Premise"/>/<see cref="AdScriptWriteRequest.Tone"/>) before it
    /// reaches the prompt — the SAME unbounded-free-text-field discipline
    /// <c>CrosstalkPromptBuilder.MaxSoulChars</c>'s own remarks document for <c>ShowName</c>/<c>Daypart</c>.</summary>
    const int MaxBriefFieldChars = 4000;

    public static string BuildSystemPrompt(AdScriptWriteRequest request)
    {
        var totalCharBudget = (int)(request.SpotSeconds * CharsPerSecond);
        var perBeatCharBudget = Math.Max(1, totalCharBudget / Beats.Length);
        var beatList = string.Join(", ", Beats);

        var scaffold =
            $"You write a {request.SpotSeconds}-second radio ad spot script for a FICTIONAL sponsor, " +
            "in this EXACT wire format, one line per turn, nothing else before or after: " +
            "\"TAG: <line>\". Use 1-3 distinct voice tags, ALL CAPS, and " +
            $"{AnnouncerTag} MUST speak at least one line. " +
            $"Keep every line under {request.MaxLineChars} characters. " +
            $"Write exactly four beats in this order - {beatList} - each roughly " +
            $"{perBeatCharBudget} characters, about {totalCharBudget} characters total across the " +
            "whole spot. Never name a real brand, company, product, or trademark - invent a fictional " +
            "one instead. Any phone number spoken must use the fictional 555 exchange, for example " +
            "555-0142. No stage directions, no emoji, no markdown formatting.";

        var postureLine = request.Posture == AudiencePosture.Everyone
            ? " Keep the language family-friendly."
            : "";

        return scaffold + postureLine;
    }

    public static string BuildUserContent(AdScriptWriteRequest request)
    {
        var lines = new List<string> { $"Brand: {Truncate(request.Brand, MaxBriefFieldChars)}" };

        if (request.Premise is { Length: > 0 } premise)
            lines.Add($"Premise: {Truncate(premise, MaxBriefFieldChars)}");
        if (request.Tone is { Length: > 0 } tone)
            lines.Add($"Tone: {Truncate(tone, MaxBriefFieldChars)}");

        lines.Add("Write the spot now.");
        return string.Join('\n', lines);
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
