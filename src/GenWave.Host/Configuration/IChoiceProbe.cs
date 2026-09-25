namespace GenWave.Host.Configuration;

/// <summary>
/// One live choice source a <see cref="SettingChoiceSource.Probe"/> setting resolves through (SPEC
/// F205.7, STORY-479, PLAN T579/T580) — the callable side of the (name, scope, fetch) triple
/// <see cref="ProbedChoiceCache"/> caches. Host-internal: T580's concrete Ollama-model and TTS-voice
/// probes are the only implementations this codebase ships, each wired into the cache singleton
/// through Program.cs — not a public plugin seam.
/// </summary>
internal interface IChoiceProbe
{
    /// <summary>
    /// The <see cref="SettingChoiceSource.Probe.Name"/> this probe answers for (e.g. the allowlist
    /// entry naming <c>Llm:Model</c> or <c>Station:Voice</c>) — <see cref="ProbedChoiceCache"/>'s own
    /// per-probe cache key and single-flight gate.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The server this probe would call right now (e.g. the live <c>Llm:Endpoint</c>/<c>Tts:Endpoint</c>
    /// setting value) — <see cref="ProbedChoiceCache"/>'s own scope key (SPEC F205.7c). A value that
    /// differs from a cached entry's own means "a different server": the cache treats any previously
    /// cached last-good list as belonging to that OLD server, not this one, even within the 60 s
    /// freshness window.
    /// </summary>
    string ScopeKey();

    /// <summary>
    /// Fetches the live choice list. <paramref name="ct"/> carries <see cref="ProbedChoiceCache"/>'s
    /// own 2 s timeout linked with the caller's token (SPEC F205.7b) — this method should let
    /// cancellation and any transport fault (timeout, connect failure, non-2xx, malformed response)
    /// simply throw; the cache is what turns either into a cached <see cref="ProbedChoiceResult.Failed"/>
    /// outcome (or, for the caller's OWN cancellation, a propagated exception instead) — never this
    /// probe's own concern.
    /// </summary>
    Task<IReadOnlyList<SettingChoice>> FetchAsync(CancellationToken ct);
}
