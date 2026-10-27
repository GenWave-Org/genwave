namespace GenWave.VoiceOff.Speech;

/// <summary>Everything the tool knows about one voice engine; read from <c>engines/&lt;slug&gt;.json</c> (SPEC F217.2, F217.13).</summary>
internal sealed record EngineProfile(
    string Slug,
    bool Baseline,
    string Container,
    int Port,
    Uri? Endpoint,
    int TimeoutSeconds,
    string License,
    string? Notes);
