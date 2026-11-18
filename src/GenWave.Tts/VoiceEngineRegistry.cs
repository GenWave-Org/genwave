namespace GenWave.Tts;

using Microsoft.Extensions.Options;

/// <summary>
/// The configured voice engines by name (SPEC F219.1). Rebuilt whenever the options instance
/// changes, so a live <c>Tts:Endpoint</c> / <c>Tts:Engines</c> save reaches the next lookup.
/// </summary>
public sealed class VoiceEngineRegistry : IVoiceEngineRegistry
{
    sealed record Snapshot(VoiceEnginesOptions Source, IReadOnlyList<VoiceEngine> All, Dictionary<string, VoiceEngine> ByName);

    readonly Func<VoiceEnginesOptions> read;
    Snapshot snapshot;

    public VoiceEngineRegistry(IOptionsMonitor<VoiceEnginesOptions> options)
        : this(() => options.CurrentValue)
    {
    }

    public VoiceEngineRegistry(IOptions<VoiceEnginesOptions> options)
        : this(() => options.Value)
    {
    }

    VoiceEngineRegistry(Func<VoiceEnginesOptions> read)
    {
        this.read = read;
        snapshot = Build(read());
    }

    public IReadOnlyList<VoiceEngine> All => Current().All;

    public VoiceEngine? Get(string name) => Current().ByName.GetValueOrDefault(name);

    Snapshot Current()
    {
        var source = read();
        var seen = Volatile.Read(ref snapshot);
        if (ReferenceEquals(seen.Source, source))
            return seen;

        var rebuilt = Build(source);
        Volatile.Write(ref snapshot, rebuilt);
        return rebuilt;
    }

    static Snapshot Build(VoiceEnginesOptions options)
    {
        var byName = new Dictionary<string, VoiceEngine>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, entry) in options.Engines)
        {
            // Unreachable after the ValidateOnStart check; the throw keeps the lookup free of `!`.
            var profile = VoiceEngineProfiles.Find(entry.Profile)
                ?? throw new InvalidOperationException(
                    $"Tts:Engines:{name}:Profile '{entry.Profile}' is not a known profile.");
            byName[name] = new VoiceEngine(name, entry.Endpoint, profile, entry.RenderBudgetSeconds);
        }

        return new Snapshot(options, [.. byName.Values], byName);
    }
}
