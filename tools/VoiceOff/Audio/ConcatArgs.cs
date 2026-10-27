using System.Globalization;

namespace GenWave.VoiceOff.Audio;

/// <summary>The ffmpeg argv that joins a card's raw paragraphs into one file with a silence between each pair (SPEC F217.5).</summary>
internal static class ConcatArgs
{
    /// <summary>
    /// Reads every paragraph, converts it to the tool's 24 kHz mono, and concatenates the paragraphs with <paramref name="pauseSeconds"/> of
    /// silence between them. The inputs are <c>file:</c>-prefixed absolute paths, so none is read as an option or a URL.
    /// </summary>
    internal static IReadOnlyList<string> Build(IReadOnlyList<string> paragraphs, double pauseSeconds, string output)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        if (paragraphs.Count == 0)
        {
            throw new ArgumentException("There must be at least one paragraph to join.", nameof(paragraphs));
        }

        string pause = pauseSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        string rate = ClipScrubber.SampleRate.ToString(CultureInfo.InvariantCulture);
        List<string> args = ["-nostdin", "-hide_banner", "-y"];
        string format = $"aformat=sample_rates={rate}:channel_layouts=mono:sample_fmts=s16";
        List<string> sequence = [];
        int input = 0;
        List<string> filters = [];
        for (int i = 0; i < paragraphs.Count; i++)
        {
            if (i > 0)
            {
                args.AddRange(["-f", "lavfi", "-t", pause, "-i", $"anullsrc=r={rate}:cl=mono"]);
                filters.Add($"[{input}:a]{format}[s{i}]");
                sequence.Add($"[s{i}]");
                input++;
            }

            args.AddRange(["-i", ClipScrubber.InputArg(paragraphs[i])]);
            filters.Add($"[{input}:a]{format}[t{i}]");
            sequence.Add($"[t{i}]");
            input++;
        }

        filters.Add($"{string.Concat(sequence)}concat=n={sequence.Count}:v=0:a=1[out]");
        args.AddRange(["-filter_complex", string.Join(';', filters), "-map", "[out]", "-c:a", "pcm_s16le", "-f", "wav", Path.GetFullPath(output)]);
        return args;
    }
}
