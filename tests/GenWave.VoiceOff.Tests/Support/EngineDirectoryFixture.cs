namespace GenWave.VoiceOff.Tests.Support;

/// <summary>Writes engine profile files into fresh temp folders and deletes them on dispose.</summary>
internal sealed class EngineDirectoryFixture : IDisposable
{
    private readonly List<string> directories = [];

    internal string Create(params (string FileName, string Json)[] files)
    {
        string directory = Path.Combine(Path.GetTempPath(), "voiceoff-engines-" + Guid.NewGuid().ToString("N"));
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
