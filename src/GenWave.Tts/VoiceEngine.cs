namespace GenWave.Tts;

/// <summary>One configured engine (SPEC F219.1): its name, where it listens, what it can do, and its own render budget (F219.6).</summary>
public sealed record VoiceEngine(
    string Name,
    string Endpoint,
    VoiceEngineProfile Profile,
    int RenderBudgetSeconds);
