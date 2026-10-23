using GenWave.VoiceOff.Render;

namespace GenWave.VoiceOff.Tests.Render;

public sealed class AtomicTextFileTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "voiceoff-atomic-" + Guid.NewGuid().ToString("N"));

    public AtomicTextFileTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public async Task TheTextLandsAtThePath()
    {
        string path = Path.Combine(directory, "a.json");
        await AtomicTextFile.WriteAsync(path, "hello", CancellationToken.None);
        Assert.Equal("hello", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task AFailedMoveLeavesNoTempFile()
    {
        // A folder sits where the file should go, so the move fails after the temp file was written.
        string path = Path.Combine(directory, "taken");
        Directory.CreateDirectory(path);
        try
        {
            await AtomicTextFile.WriteAsync(path, "hello", CancellationToken.None);
        }
        catch (IOException)
        {
            // Expected: the move cannot replace a folder.
        }

        Assert.False(File.Exists(path + ".tmp"));
    }
}
