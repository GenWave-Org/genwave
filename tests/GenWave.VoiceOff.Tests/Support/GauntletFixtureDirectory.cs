namespace GenWave.VoiceOff.Tests.Support;

/// <summary>Writes small gauntlet directories into fresh temp folders and deletes them on dispose.</summary>
internal sealed class GauntletFixtureDirectory : IDisposable
{
    internal const string EightWords = "one two three four five six seven eight";

    private readonly List<string> directories = [];

    internal const string ValidCorrections = """{ "provenance": "test", "corrections": [] }""";
    internal const string ValidPronunciations = """{ "provenance": "test", "pronunciations": [] }""";

    /// <summary>Writes the given files; a rules file left out is written with valid empty rules so only the defect under test fails.</summary>
    internal string Create(params (string FileName, string Json)[] files)
    {
        string directory = Path.Combine(Path.GetTempPath(), "voiceoff-gauntlet-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        directories.Add(directory);
        foreach ((string fileName, string json) in files)
        {
            File.WriteAllText(Path.Combine(directory, fileName), json);
        }

        WriteIfAbsent(directory, "corrections.json", ValidCorrections);
        WriteIfAbsent(directory, "pronunciations.json", ValidPronunciations);

        return directory;
    }

    /// <summary>Like <see cref="Create"/> but writes no default rules files.</summary>
    internal string CreateBare(params (string FileName, string Json)[] files)
    {
        string directory = Path.Combine(Path.GetTempPath(), "voiceoff-gauntlet-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        directories.Add(directory);
        foreach ((string fileName, string json) in files)
        {
            File.WriteAllText(Path.Combine(directory, fileName), json);
        }

        return directory;
    }

    internal static string Line(string id, string lineClass, string text, string cues = "[]") =>
        $$"""{ "id": "{{id}}", "class": "{{lineClass}}", "register": "bubbly", "text": "{{text}}", "cues": {{cues}} }""";

    internal static string Lines(params string[] lines) => $$"""{ "lines": [ {{string.Join(", ", lines)}} ] }""";

    private static void WriteIfAbsent(string directory, string fileName, string json)
    {
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, json);
        }
    }

    public void Dispose()
    {
        foreach (string directory in directories)
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
