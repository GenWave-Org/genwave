namespace GenWave.Host.Voices;

/// <summary>
/// Tells a WAV, FLAC or MP3 upload by its first bytes (SPEC F219.12) and names the ffmpeg demuxer for it. The demuxer is forced on
/// the command line so ffmpeg never guesses a container from client-supplied bytes (playlist and concat demuxers can reach other
/// files and hosts).
/// </summary>
internal static class VoiceClipContainer
{
    public const int HeaderBytes = 12;

    /// <summary>The ffmpeg demuxer name (<c>wav</c>, <c>flac</c>, <c>mp3</c>), or null when the bytes are none of the three.</summary>
    public static string? Sniff(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WAVE"u8))
            return "wav";
        if (header.Length >= 4 && header[..4].SequenceEqual("fLaC"u8))
            return "flac";
        if (header.Length >= 3 && header[..3].SequenceEqual("ID3"u8))
            return "mp3";
        if (header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
            return "mp3";

        return null;
    }
}
