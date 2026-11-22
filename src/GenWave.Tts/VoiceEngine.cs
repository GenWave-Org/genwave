namespace GenWave.Tts;

/// <summary>One configured engine (SPEC F219.1): its name, where it listens, what it can do, and its own render budget (F219.6).</summary>
public sealed record VoiceEngine(
    string Name,
    string Endpoint,
    VoiceEngineProfile Profile,
    int RenderBudgetSeconds)
{
    /// <summary>The <see cref="IDependencyHealth"/> key this engine's liveness is read under (kokoro and piper are probed; others read no verdict).</summary>
    public string DependencyName => Profile.Name;

    /// <summary>Name plus endpoint: one engine instance, so a repoint is a different identity (warm-up and render evidence key on it).</summary>
    public string Identity => $"{Name}|{Endpoint}";
}
