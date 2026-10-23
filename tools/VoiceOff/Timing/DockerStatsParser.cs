using System.Globalization;
using System.Text.RegularExpressions;

namespace GenWave.VoiceOff.Timing;

/// <summary>
/// Reads the <c>{{.MemUsage}}</c> text of <c>docker stats --no-stream</c> (SPEC F214.7), e.g. <c>2.884GiB / 4GiB</c>.
/// Only the left side (usage) is read. "MB" in this tool means MiB rounded to the nearest whole number, so
/// 1.2GiB is 1228.8 MiB and reports as 1229. docker prints binary units (B, KiB, MiB, GiB, TiB); decimal
/// units (kB, MB, GB, TB) are accepted too. A line that is not a usage reading (such as <c>-- / --</c>) is not a sample.
/// </summary>
internal static partial class DockerStatsParser
{
    private const double BytesPerMib = 1024d * 1024d;

    /// <summary>The usage in MiB from the first non-empty line, or null when the text is not a usage reading.</summary>
    internal static double? ParseMib(string? output)
    {
        string? line = (output ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (line is null)
        {
            return null;
        }

        string usage = line.Split('/')[0].Trim();
        Match match = Usage().Match(usage);
        if (!match.Success
            || !double.TryParse(match.Groups["n"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double amount)
            || BytesPerUnit(match.Groups["u"].Value) is not double bytes)
        {
            return null;
        }

        return amount * bytes / BytesPerMib;
    }

    private static double? BytesPerUnit(string unit) => unit switch
    {
        "B" => 1,
        "KiB" => 1024d,
        "MiB" => BytesPerMib,
        "GiB" => BytesPerMib * 1024d,
        "TiB" => BytesPerMib * 1024d * 1024d,
        "kB" => 1e3,
        "MB" => 1e6,
        "GB" => 1e9,
        "TB" => 1e12,
        _ => null,
    };

    [GeneratedRegex(@"^(?<n>\d+(?:\.\d+)?)\s*(?<u>[A-Za-z]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex Usage();
}
