namespace GenWave.MediaLibrary.Options;

/// <summary>
/// The mood tagger's view of the SAME configured LLM endpoint <c>GenWave.Tts.LlmOptions</c> already
/// binds (SPEC F85.2 — "the configured LLM endpoint", singular, not a second one an operator would
/// need to duplicate). Deliberately its OWN options class bound to the identical <c>"Llm"</c>
/// configuration section rather than a <c>ProjectReference</c> to <c>GenWave.Tts</c> —
/// <c>GenWave.MediaLibrary</c> must never depend on <c>GenWave.Tts</c> (see
/// <c>GenWave.MediaLibrary.Enrich.EnrichmentService</c>'s own remarks on the boundary). The .NET
/// options binder keys purely off the section path string, so this class and
/// <c>GenWave.Tts.LlmOptions</c> both resolve the SAME live configuration value — an operator's
/// <c>PUT /api/settings</c> edit to <c>Llm:Endpoint</c> reaches both readers identically, with no
/// double-configuration and no api restart, mirroring every other <c>IOptionsMonitor</c> leaf in
/// this codebase. <c>GenWave.Tts.LlmOptions</c> already carries its own <c>ValidateOnStart</c> for
/// this exact section (bound once, at Host composition), so this class adds none of its own.
/// </summary>
public sealed class MoodTaggerOptions
{
    public const string Section = "Llm";

    /// <summary>OpenAI-compatible chat-completions base URL. Empty = the LLM is unconfigured (F85.3).</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>Model name passed to the completions call.</summary>
    public string Model { get; set; } = "";

    /// <summary>Per-completion budget in seconds; a miss counts as a failed round trip (F85.2).</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Optional bearer token for the endpoint — env-only per the F19.3 secrets rule.</summary>
    public string ApiKey { get; set; } = "";
}
