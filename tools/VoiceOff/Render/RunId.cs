using System.Globalization;
using System.Text.RegularExpressions;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Render;

/// <summary>A run's folder name: the UTC start time and the tier, e.g. <c>20261022T193000Z-cpu</c>.</summary>
internal static partial class RunId
{
    internal static string Format(DateTimeOffset startedAt, EngineTier tier) =>
        startedAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" + TierName(tier);

    /// <summary>Reads a folder name written by <see cref="Format"/>; false for any other name.</summary>
    internal static bool TryParseTier(string name, out EngineTier tier)
    {
        ArgumentNullException.ThrowIfNull(name);
        Match match = Shape().Match(name);
        tier = match.Success && match.Groups["tier"].Value == "gpu" ? EngineTier.Gpu : EngineTier.Cpu;
        return match.Success;
    }

    [GeneratedRegex(@"\A\d{8}T\d{6}Z-(?<tier>cpu|gpu)\z", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();

    internal static string TierName(EngineTier tier) => tier switch
    {
        EngineTier.Cpu => "cpu",
        EngineTier.Gpu => "gpu",
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown tier."),
    };
}
