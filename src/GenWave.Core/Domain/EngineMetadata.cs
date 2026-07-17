using System.Globalization;

namespace GenWave.Core.Domain;

/// <summary>
/// The current on-air metadata as reported by the OUTPUT (PRD §0). A track we pushed carries the
/// <c>track_id</c> we stamped; the dead-air safe rotation does not — that absence is exactly how the
/// feeder detects a drained queue and self-heals.
/// </summary>
public readonly record struct EngineMetadata(IReadOnlyDictionary<string, string> Values)
{
    /// <summary>
    /// True if the current on-air track carries our stamped media id (the <c>track_id</c> field the
    /// feeder pushes and genwave.liq exports onto the output metadata). Its ABSENCE means the safe
    /// rotation is airing ⇒ the queue drained (PRD §0 req 2/3). This is the signal the pull-based
    /// feeder keys on — advancement is a change in this id, never RID arithmetic.
    /// </summary>
    public bool TryGetMediaId(out string mediaId)
    {
        if (Values.TryGetValue("track_id", out var raw) && raw.Length > 0)
        {
            mediaId = raw;
            return true;
        }

        mediaId = string.Empty;
        return false;
    }

    /// <summary>
    /// Extracts the annotation fields stamped by the feeder or the safe-track endpoint for an
    /// engine-initiated play — a track the C# feeder did not push via <c>PushAsync</c>. Reads
    /// <c>title</c> and <c>artist</c> from the standard tag fields; parses <c>gainDb</c> from the
    /// <c>replay_gain</c> annotation (format: <c>"X.XX dB"</c>).
    /// <para>
    /// <c>amplify</c> only READS its <c>override="replay_gain"</c> key — it never deletes it; the
    /// key's presence or absence on the OUTPUT metadata dict is gated entirely by genwave.liq's
    /// <c>settings.encoder.metadata.export</c> allow-list (source-verified against pinned Liquidsoap
    /// v2.4.4, 2026-07-13 — see docs/ARCHITECTURE.md "On-air metadata fidelity"). Before F37,
    /// <c>replay_gain</c> was absent from that list, so it never reached the output dict for ANY
    /// track regardless of amplify. After F37 (F37.2), <c>replay_gain</c> joins the export list and
    /// the safe branch is wrapped in <c>amplify</c> too (F37.1) — so a safe-rotation play's stamped
    /// gain is both applied to the audio AND exported to this method. Feeder-pushed tracks still
    /// source gainDb from <c>pushedMeta</c> instead and never reach this method.
    /// Missing or unparseable fields degrade to null/0 — never throws (F7.4).
    /// </para>
    /// </summary>
    public (string? Title, string? Artist, double GainDb) ExtractAnnotations()
    {
        var title = Values.TryGetValue("title", out var t) && t.Length > 0 ? t : (string?)null;
        var artist = Values.TryGetValue("artist", out var a) && a.Length > 0 ? a : (string?)null;
        var gainDb = ParseReplayGain();
        return (title, artist, gainDb);
    }

    /// <summary>
    /// Parses the <c>replay_gain</c> annotation value (e.g. <c>"-3.50 dB"</c>) into a <c>double</c>.
    /// Returns 0.0 if the field is absent or unparseable (F7.4).
    /// </summary>
    double ParseReplayGain()
    {
        if (!Values.TryGetValue("replay_gain", out var raw) || raw.Length == 0)
            return 0.0;

        // Annotation format: "X.XX dB". Strip the suffix and parse the numeric part.
        var numeric = raw.Trim();
        if (numeric.EndsWith(" dB", StringComparison.OrdinalIgnoreCase))
            numeric = numeric[..^3].TrimEnd();

        return double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var db)
            ? db
            : 0.0;
    }
}
