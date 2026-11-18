namespace GenWave.Tts;

/// <summary>Writes rendered audio bytes to the transient render path (see <see cref="TransientRenderPath"/>).</summary>
static class TransientAudioWriter
{
    public static async Task<string> WriteAsync(TtsOptions cfg, byte[] bytes, CancellationToken ct)
    {
        var path = TransientRenderPath.For(cfg);

        // Path.GetDirectoryName is non-null for a Path.Combine result with a non-empty CacheRoot.
        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllBytesAsync(path, bytes, ct);
        return path;
    }
}
