using System.Globalization;

namespace GenWave.VoiceOff.Score;

/// <summary>How a win rate prints: two decimals, invariant culture; a missing rate is an em dash.</summary>
internal static class Rate
{
    internal const string Missing = "—";

    internal static string Format(double? rate) =>
        rate is double value ? value.ToString("0.00", CultureInfo.InvariantCulture) : Missing;
}
