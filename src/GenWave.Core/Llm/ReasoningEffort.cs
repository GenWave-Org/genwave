namespace GenWave.Core.Llm;

/// <summary>
/// The one vocabulary for the <c>Llm:ReasoningEffort</c> setting (gh-#620) and the ONE place its
/// configured value becomes the OpenAI-compatible <c>reasoning_effort</c> request field every
/// completions poster in this solution sends (<c>LlmCopyWriter</c>, <c>CrosstalkScriptWriter</c>,
/// <c>LlmWishParser</c>, <c>OllamaMoodTagger</c>, <c>OllamaExplicitClassifier</c>).
///
/// <para>
/// <b>Why it exists.</b> Thinking-capable models (gemma4, qwen3, deepseek-r1, magistral on Ollama's
/// <c>/v1/chat/completions</c>) put their chain-of-thought in a separate <c>message.reasoning</c>
/// field and only start the answer once reasoning finishes; against the per-call <c>max_tokens</c>
/// cap the reasoning alone exhausts the budget, generation dies at <c>finish_reason: "length"</c>, and
/// <c>content</c> comes back empty — every call, a template fallback that masks a 100% outage.
/// <c>"reasoning_effort": "none"</c> on the request is the wire-verified cure, so it is the default.
/// </para>
///
/// <para>
/// <b>Why it is a knob, not a constant.</b> <c>Llm:Endpoint</c> is operator-set: a third-party
/// OpenAI-compatible backend may reject an unknown field outright, so <see cref="Omit"/> keeps the
/// escape hatch (the field is not sent at all — the pre-gh-#620 wire shape, byte for byte). The
/// three effort levels ride along for operators who WANT a thinking model to think.
/// </para>
/// </summary>
public static class ReasoningEffort
{
    public const string None = "none";
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";

    /// <summary>Do not send the field at all — the pre-gh-#620 request shape.</summary>
    public const string Omit = "omit";

    /// <summary>The shipped default: answer directly, no chain-of-thought.</summary>
    public const string Default = None;

    /// <summary>Every accepted setting value, lowercase, in the order the admin UI lists them.</summary>
    public static readonly IReadOnlyList<string> Accepted = [None, Low, Medium, High, Omit];

    /// <summary>
    /// Whether <paramref name="value"/> is one of <see cref="Accepted"/> (case-insensitive,
    /// surrounding whitespace ignored — the <c>Llm:DegradationPin</c> guard's own tolerance). Empty
    /// is NOT accepted: the setting always names a posture; "send nothing" is spelled
    /// <see cref="Omit"/>, never blank.
    /// </summary>
    public static bool IsValid(string? value) => Normalize(value) is not null;

    /// <summary>
    /// The value to put on the wire for a configured setting: the lowercase effort level, or
    /// <see langword="null"/> meaning <em>leave the field out of the request</em>. Fails SAFE for the
    /// shipped backend — anything unrecognized (garbage from a hand-edited env, a blank) becomes
    /// <see cref="Default"/> rather than an unknown string Ollama would 400 on, and the validator has
    /// already refused it at the settings API, so this arm is only ever reachable from outside that
    /// door.
    /// </summary>
    public static string? ToWire(string? configured)
    {
        var normalized = Normalize(configured) ?? Default;
        return normalized == Omit ? null : normalized;
    }

    static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var candidate = value.Trim().ToLowerInvariant();
        return Accepted.Contains(candidate) ? candidate : null;
    }
}
