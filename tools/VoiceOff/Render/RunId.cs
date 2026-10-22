using System.Globalization;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Render;

/// <summary>A run's folder name: the UTC start time and the tier, e.g. <c>20261022T193000Z-cpu</c>.</summary>
internal static class RunId
{
    internal static string Format(DateTimeOffset startedAt, EngineTier tier) =>
        startedAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" + TierName(tier);

    internal static string TierName(EngineTier tier) => tier switch
    {
        EngineTier.Cpu => "cpu",
        EngineTier.Gpu => "gpu",
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown tier."),
    };
}
