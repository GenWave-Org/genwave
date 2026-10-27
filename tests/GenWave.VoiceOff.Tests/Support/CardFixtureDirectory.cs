namespace GenWave.VoiceOff.Tests.Support;

/// <summary>Writes small card sets into fresh temp folders and deletes them on dispose.</summary>
internal sealed class CardFixtureDirectory : IDisposable
{
    private readonly List<string> directories = [];

    /// <summary>A valid card for <paramref name="engine"/>; the extras are raw JSON members spliced in before <c>why</c>.</summary>
    internal static string CardJson(
        string id,
        string engine,
        string host = "thor-cpu",
        string cues = """{ "laugh": "Haha!", "breath": null }""",
        string settings = """{ "alpha": 0.3, "nested": { "keep": [1, "two", null] } }""",
        string extraMembers = "") =>
        CardJson(id, engine, host, cues, settings, extraMembers, extraMembers.Contains("\"asShipped\"", StringComparison.Ordinal) ? "{}" : """{ "4": "Sorry. Allergies." }""");

    // The as-shipped card takes no overrides, so a card marked asShipped gets an empty object.
    private static string CardJson(string id, string engine, string host, string cues, string settings, string extraMembers, string overrides) =>
        $$"""
        {
          "id": "{{id}}",
          "engine": "{{engine}}",
          "title": "Card {{id}}",
          "host": "{{host}}",
          "voice": "voice-a",
          "speed": 1.1,
          "settings": {{settings}},
          "cues": {{cues}},
          "overrides": {{overrides}},
          "facts": { "checkpoint": "cp-1", "voices": 54, "cloning": false, "ownVoice": true, "cannot": ["breath", "sigh"], "tricks": [] },
          {{extraMembers}}
          "why": "Because {{id}}."
        }
        """;

    internal string Create(params (string FileName, string Json)[] files)
    {
        string directory = Path.Combine(Path.GetTempPath(), "voiceoff-cards-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        directories.Add(directory);
        foreach ((string fileName, string json) in files)
        {
            File.WriteAllText(Path.Combine(directory, fileName), json);
        }

        return directory;
    }

    public void Dispose()
    {
        foreach (string directory in directories)
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
