using System.Globalization;
using System.Text.Json;

namespace GenWave.VoiceOff.Audio;

/// <summary>The first-pass <c>loudnorm</c> JSON report, kept as ffmpeg's own strings so the second pass echoes them exactly.</summary>
internal sealed record LoudnormMeasurement(string InputI, string InputTp, string InputLra, string InputThresh, string TargetOffset)
{
    /// <summary><see cref="InputI"/> as a number; <see cref="TryParse"/> only ever builds a finite one.</summary>
    internal double IntegratedLufs => double.Parse(InputI, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary><see cref="InputTp"/> as a number, finite by the same rule.</summary>
    internal double TruePeakDbtp => double.Parse(InputTp, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>Parses the last JSON block on ffmpeg's stderr; null when absent or any value is not a finite number (silence reports -inf).</summary>
    internal static LoudnormMeasurement? TryParse(string stderr)
    {
        using JsonDocument? doc = ParseTrailingBlock(stderr);
        if (doc is null)
        {
            return null;
        }

        JsonElement root = doc.RootElement;
        string? i = Finite(root, "input_i");
        string? tp = Finite(root, "input_tp");
        string? lra = Finite(root, "input_lra");
        string? thresh = Finite(root, "input_thresh");
        string? offset = Finite(root, "target_offset");
        return i is null || tp is null || lra is null || thresh is null || offset is null
            ? null
            : new LoudnormMeasurement(i, tp, lra, thresh, offset);
    }

    /// <summary>True when the second-pass report on ffmpeg's stderr says <c>"normalization_type" : "linear"</c>; false for dynamic or anything unreadable.</summary>
    internal static bool IsLinear(string stderr)
    {
        using JsonDocument? doc = ParseTrailingBlock(stderr);
        return doc is not null
            && doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("normalization_type", out JsonElement type)
            && type.ValueKind == JsonValueKind.String
            && type.GetString() == "linear";
    }

    // The last { ... } block on ffmpeg's stderr as JSON; null when absent or malformed.
    private static JsonDocument? ParseTrailingBlock(string stderr)
    {
        int start = stderr.LastIndexOf('{');
        int end = stderr.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(stderr[start..(end + 1)]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Finite(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? text = value.GetString();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d)
            ? text
            : null;
    }
}
