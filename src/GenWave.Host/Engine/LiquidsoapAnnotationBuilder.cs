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
    /// <param name="artworkUrl">
    /// The item's resolved artwork/station-icon URL (SPEC F88.4–F88.5, STORY-223, PLAN T85), or
    /// <see langword="null"/>/empty to omit the key entirely — the F88.5 "empty base ⇒ no url=
    /// anywhere" contract, and this method's own honest-absence discipline (same shape as
    /// <see cref="MediaItem.Cue"/>/<see cref="MediaItem.IntroEnergy"/> above). A caller resolves
    /// the value through the async, I/O-bearing <see cref="ArtworkUrlResolver"/> and passes a
    /// plain string here, so this method itself stays pure and synchronous.
    /// </param>
    /// <returns>
    /// A single-line string of the form
    /// <c>annotate:track_id="…",station_id="…",...,title="…":/locator</c>.
    /// </returns>
    public static string Build(
        MediaItem item, double gainDb, string stationId, string stationName, string? artworkUrl = null)
    {
        // F195.1 (STORY-462, PLAN T541, gh-#699): a voice item is a `tts:`-prefixed render OR any
        // item carrying a SegmentKind — a vended ad spot (SegmentKind.Ad) and a pooled authored
        // station ident (SegmentKind.StationId, F110.2) are library rows with numeric MediaIds, so
        // the tts: prefix alone misses them; the kind fact travels on the item instead.
        var isVoice = item.MediaId.StartsWith("tts:", StringComparison.Ordinal) || item.SegmentKind is not null;
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
        // gh-#80: a voice item shorter than the cross() window makes Liquidsoap hit end-of-track
        // while buffering and warn "crossfade duration is longer than the track's duration"
        // (observed as an on-air stutter, 2026-07-22). liq_cross_duration is cross()'s built-in
        // per-track override (its default override_duration key, v2.4.4 cross.ml): stamping
        // half the item's measured duration — clamped to [0.2s, 3.0s] — bounds the window on
        // BOTH sides of the item (the override is processed even while the item's head is
        // being buffered as the incoming track) and auto-resets on the next track. Music tracks
        // never carry it; when duration is unmeasured (null DurationMs) nothing is stamped —
        // same honest-absence rule as every other enrichment field here. This only bounds the
        // window; the transition arm itself is picked off gw_tts below (genwave.liq gw_transition).
        var voiceCrossField = isVoice && item.DurationMs is { } durationMs
            ? $"liq_cross_duration=\"{Math.Clamp(durationMs / 1000.0 * 0.5, 0.2, 3.0).ToString("F2", CultureInfo.InvariantCulture)}\","
            : string.Empty;
        // SPEC F88.4–F88.5 (STORY-223, PLAN T85): omit-when-empty, same discipline as cueFields/
        // energyFields above — a blank Station:PublicBaseUrl (the default) means artworkUrl always
        // arrives null/empty here, so this annotation stays byte-identical to every pre-F88 push.
        var urlField = string.IsNullOrEmpty(artworkUrl)
            ? string.Empty
            : $"url=\"{Escape(artworkUrl)}\",";

        return
            $"annotate:track_id=\"{Escape(item.MediaId)}\"," +
            $"station_id=\"{Escape(stationId)}\"," +
            $"station_name=\"{Escape(stationName)}\"," +
            // F195.2 (STORY-462): gw_tts="true" is what puts a vended ad spot's tail into the
            // music->voice duck arm instead of a crossfade under the next track.
            $"gw_tts=\"{(isVoice ? "true" : "false")}\"," +
            $"replay_gain=\"{gainDb.ToString("0.00", CultureInfo.InvariantCulture)} dB\"," +
            artistField +
            cueFields +
            urlField +
            energyFields +
            voiceCrossField +
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
