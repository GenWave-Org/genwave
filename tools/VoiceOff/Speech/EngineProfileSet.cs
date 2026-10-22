namespace GenWave.VoiceOff.Speech;

/// <summary>Every engine profile in an <c>engines</c> directory, with exactly one baseline.</summary>
internal sealed class EngineProfileSet
{
    private EngineProfileSet(IReadOnlyList<EngineProfile> profiles, EngineProfile baseline)
    {
        Profiles = profiles;
        Baseline = baseline;
    }

    internal IReadOnlyList<EngineProfile> Profiles { get; }

    internal EngineProfile Baseline { get; }

    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    /// <exception cref="InvalidDataException">A profile is malformed, or the set does not have exactly one baseline.</exception>
    internal static EngineProfileSet Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Engines directory not found: {directory}");
        }

        List<(string File, EngineProfile Profile)> loaded = [];
        foreach (string path in Directory.GetFiles(directory, "*.json").OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            loaded.Add((name, EngineProfileReader.Parse(File.ReadAllText(path), name)));
        }

        string[] baselines = loaded.Where(l => l.Profile.Baseline).Select(l => l.File).ToArray();
        return baselines.Length switch
        {
            1 => new EngineProfileSet(loaded.Select(l => l.Profile).ToList(), loaded.First(l => l.Profile.Baseline).Profile),
            0 => throw new InvalidDataException($"No engine profile in {directory} has baseline true; exactly one must."),
            _ => throw new InvalidDataException($"Engine profiles {string.Join(", ", baselines)} all have baseline true; exactly one must."),
        };
    }
}
