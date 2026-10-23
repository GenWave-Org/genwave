using System.Globalization;
using GenWave.Loudness;

namespace GenWave.VoiceOff.Speech;

/// <summary>
/// A short feature vector of a render, taken from ffmpeg's <c>astats</c> on a 16 kHz mono copy, used to
/// tell whether two renders are the same audio without listening (SPEC F214.6).
/// </summary>
internal sealed class AudioFingerprint
{
    private const double SilenceFloorDb = -120.0;
    private const string ChannelPrefix = "lavfi.astats.1.";

    // Each feature is divided by a typical spread so no one of them dominates the distance.
    private static readonly (string Key, double Scale)[] FeatureScales =
    [
        ("RMS_level", 10.0),
        ("Peak_level", 10.0),
        ("Zero_crossings_rate", 0.05),
        ("Entropy", 0.5),
    ];

    internal AudioFingerprint(IReadOnlyList<double> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        Features = features;
    }

    /// <summary>The normalized features, in a fixed order.</summary>
    internal IReadOnlyList<double> Features { get; }

    /// <summary>Euclidean distance between two fingerprints of the same length.</summary>
    internal static double Distance(AudioFingerprint a, AudioFingerprint b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Features.Count != b.Features.Count)
        {
            throw new ArgumentException("Fingerprints have different lengths.", nameof(b));
        }

        double sum = 0;
        for (int i = 0; i < a.Features.Count; i++)
        {
            double delta = a.Features[i] - b.Features[i];
            sum += delta * delta;
        }

        return Math.Sqrt(sum);
    }

    /// <exception cref="InvalidOperationException">ffmpeg failed, or its report lacks a feature or has an unreadable value.</exception>
    internal static async Task<AudioFingerprint> FromFileAsync(string audioPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioPath);
        string statsPath = Path.Combine(Path.GetTempPath(), "voiceoff-astats-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            await FfmpegProcess.RunFfmpegAsync(
            [
                "-hide_banner", "-nostats", "-v", "error", "-i", audioPath,
                "-af", $"aresample=16000,aformat=channel_layouts=mono,astats=metadata=1:reset=0,ametadata=print:file={FilterEscape(statsPath)}",
                "-f", "null", "-",
            ], ct);
            return Parse(await File.ReadAllLinesAsync(statsPath, ct), audioPath);
        }
        finally
        {
            File.Delete(statsPath);
        }
    }

    internal static AudioFingerprint Parse(IEnumerable<string> statLines, string source)
    {
        // astats prints running totals per frame; the last value of each key is the whole-file one.
        Dictionary<string, double> last = new(StringComparer.Ordinal);
        foreach (string line in statLines)
        {
            int eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0 && line.StartsWith(ChannelPrefix, StringComparison.Ordinal))
            {
                last[line[ChannelPrefix.Length..eq]] = ParseValue(line[(eq + 1)..], source);
            }
        }

        List<double> features = [];
        foreach ((string key, double scale) in FeatureScales)
        {
            if (!last.TryGetValue(key, out double value))
            {
                throw new InvalidOperationException($"ffmpeg astats gave no {key} for {source}.");
            }

            features.Add(value / scale);
        }

        return new AudioFingerprint(features);
    }

    // ffmpeg prints "-inf" for the level of pure silence; any other non-number is a broken report, not silence.
    private static double ParseValue(string text, string source)
    {
        string trimmed = text.Trim();
        if (string.Equals(trimmed, "-inf", StringComparison.OrdinalIgnoreCase))
        {
            return SilenceFloorDb;
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value)
            ? value
            : throw new InvalidOperationException($"ffmpeg astats gave the unreadable value \"{trimmed}\" for {source}.");
    }

    // Quotes the file name for the filter graph: wrapped in single quotes (so a colon or comma is literal),
    // with a backslash doubled and an embedded single quote closed, escaped and reopened.
    private static string FilterEscape(string path) =>
        "'" + path.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
