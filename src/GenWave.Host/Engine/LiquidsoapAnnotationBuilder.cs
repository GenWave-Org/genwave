using System.Globalization;
using GenWave.Core.Domain;

namespace GenWave.Host.Engine;

/// <summary>
/// Builds the <c>annotate:...:/path</c> string for Liquidsoap queue push commands.
/// Shared by <see cref="LiquidsoapControl.PushAsync"/> and the safe-track endpoint (K2b) so
/// both produce byte-identical annotation strings.
/// </summary>
public static class LiquidsoapAnnotationBuilder
{
    /// <summary>
    /// Builds the full <c>annotate:</c> clause for a <see cref="MediaItem"/>.
    /// </summary>
    /// <param name="item">The media item to push.</param>
    /// <param name="gainDb">Target replay-gain correction in dB.</param>
    /// <param name="stationId">Station identifier stamped onto every track (issue gitea-#148).</param>
    /// <param name="stationName">Station display name stamped onto every track (issue gitea-#148).</param>
    /// <returns>
    /// A single-line string of the form
    /// <c>annotate:track_id="…",station_id="…",...,title="…":/locator</c>.
    /// </returns>
    public static string Build(MediaItem item, double gainDb, string stationId, string stationName)
    {
        var isTts = item.MediaId.StartsWith("tts:", StringComparison.Ordinal);
        // F38.1: artist is always stamped — explicitly empty when the row has none. The telnet
        // metadata ring never merges keys across tracks, so an OMITTED artist lets a prior track's
        // value fill from the file's own embedded tag at request resolution (the gitea-#199 bleed); a
        // present-but-empty value blocks that fill and overwrites-by-presence instead.
        var artistField = string.IsNullOrWhiteSpace(item.Artist)
            ? "artist=\"\","
            : $"artist=\"{Escape(item.Artist)}\",";
        var cueFields = item.Cue is { } cue
            ? $"liq_cue_in=\"{Escape(cue.CueInSec.ToString("F2", CultureInfo.InvariantCulture))}\"," +
              $"liq_cue_out=\"{Escape(cue.CueOutSec.ToString("F2", CultureInfo.InvariantCulture))}\","
            : string.Empty;
        var energyFields = item.IntroEnergy is { } intro && item.OutroEnergy is { } outro
            ? $"gw_intro_energy=\"{Escape(intro.ToString("G", CultureInfo.InvariantCulture))}\"," +
              $"gw_outro_energy=\"{Escape(outro.ToString("G", CultureInfo.InvariantCulture))}\","
            : string.Empty;

        return
            $"annotate:track_id=\"{Escape(item.MediaId)}\"," +
            $"station_id=\"{Escape(stationId)}\"," +
            $"station_name=\"{Escape(stationName)}\"," +
            $"gw_tts=\"{(isTts ? "true" : "false")}\"," +
            $"replay_gain=\"{gainDb.ToString("0.00", CultureInfo.InvariantCulture)} dB\"," +
            artistField +
            cueFields +
            energyFields +
            $"title=\"{Escape(item.Title)}\":{item.Locator}";
    }

    /// <summary>
    /// Escapes backslashes and quotes for Liquidsoap annotation values. CR/LF are replaced with
    /// spaces to prevent a title containing a newline from splitting the single-line telnet command
    /// and corrupting the protocol framing.
    /// </summary>
    internal static string Escape(string s) => s
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\r", " ")
        .Replace("\n", " ");
}
