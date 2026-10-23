using GenWave.VoiceOff.Render;

namespace GenWave.VoiceOff.Pairing;

/// <summary>
/// The canonical bytes of each source wav, built once per path: the file is read and parsed a single time, however many pairs reuse it.
/// A wav that is missing or that cannot be read answers null. Never throws on malformed audio.
/// </summary>
internal sealed class CanonicalClips
{
    private readonly Dictionary<string, byte[]?> byPath = new(StringComparer.Ordinal);

    internal byte[]? Get(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!byPath.TryGetValue(path, out byte[]? canonical))
        {
            canonical = Build(path);
            byPath[path] = canonical;
        }

        return canonical;
    }

    private static byte[]? Build(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] bytes = File.ReadAllBytes(path);
        return WavInfo.Read(bytes, out _) is null ? null : CanonicalWav.Rewrite(bytes, out _);
    }
}
