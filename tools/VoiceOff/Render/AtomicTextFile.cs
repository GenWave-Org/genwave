namespace GenWave.VoiceOff.Render;

/// <summary>Writes a file so a reader sees the old text or the new text, never half of it: a temp file in the same folder, then a move.</summary>
internal static class AtomicTextFile
{
    internal static async Task WriteAsync(string path, string text, CancellationToken ct)
    {
        string temp = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, text, ct);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
    }
}
