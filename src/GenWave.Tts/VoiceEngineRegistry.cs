namespace GenWave.Tts;

using Microsoft.Extensions.Options;

/// <summary>The configured voice engines by name, built once at boot (SPEC F219.1).</summary>
public sealed class VoiceEngineRegistry : IVoiceEngineRegistry
{
    readonly Dictionary<string, VoiceEngine> engines;

    public VoiceEngineRegistry(IOptions<VoiceEnginesOptions> options)
    {
        engines = new Dictionary<string, VoiceEngine>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, entry) in options.Value.Engines)
        {
            // Unreachable after the ValidateOnStart check; the throw keeps the lookup free of `!`.
            var profile = VoiceEngineProfiles.Find(entry.Profile)
                ?? throw new InvalidOperationException(
                    $"Tts:Engines:{name}:Profile '{entry.Profile}' is not a known profile.");
            engines[name] = new VoiceEngine(name, entry.Endpoint, profile, entry.RenderBudgetSeconds);
        }

        All = [.. engines.Values];
    }

    public IReadOnlyList<VoiceEngine> All { get; }

    public VoiceEngine? Get(string name) => engines.GetValueOrDefault(name);
}
