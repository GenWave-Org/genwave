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
    /// <c>replay_gain</c> annotation (format: <c>"X.XX dB"</c>); reads <c>artworkUrl</c> straight off
    /// the <c>url</c> field (SPEC F88.4, F93.3, PLAN T125) — the exact <c>url=</c> annotation value
    /// the pushing endpoint (feeder or <c>/internal/safe-track</c>) stamped, echoed back unchanged by
    /// genwave.liq's own export list (<c>engine/genwave.liq</c> comment: "'url' joins the list per
    /// F88.4/T93's live-run finding"). No second HTTP/DB round trip — this is the SAME output-metadata
    /// read the feeder already performs every tick to detect an advance at all (SPEC F16.6/F93.4).
    /// <para>
    /// <c>amplify</c> only READS its <c>override="replay_gain"</c> key — it never deletes it; the
    /// key's presence or absence on the OUTPUT metadata dict is gated entirely by genwave.liq's
    /// <c>settings.encoder.metadata.export</c> allow-list (source-verified against pinned Liquidsoap
    /// v2.4.4, 2026-07-13 — see docs/ARCHITECTURE.md "On-air metadata fidelity"). Before F37,
    /// <c>replay_gain</c> was absent from that list, so it never reached the output dict for ANY
    /// track regardless of amplify. After F37 (F37.2), <c>replay_gain</c> joins the export list and
    /// the safe branch is wrapped in <c>amplify</c> too (F37.1) — so a safe-rotation play's stamped
    /// gain is both applied to the audio AND exported to this method. Feeder-pushed tracks still
    /// source gainDb (and, per F93.3, artworkUrl) from <c>pushedMeta</c> instead and never reach this
    /// method for those fields.
    /// Missing or unparseable fields degrade to null/0 — never throws (F7.4).
    /// </para>
    /// <para>
    /// <b>CALLER TRUST BOUNDARY (PLAN T125 review F2):</b> this method itself performs NO validation
    /// of <c>ArtworkUrl</c>'s provenance — it is a raw, unconditional echo of whatever the <c>url</c>
    /// field carries, which for a play this station never pushed (the safe rotation reading a file
    /// directly) can be that FILE's own embedded tag (Vorbis <c>URL=</c>, ID3 <c>W...</c>/<c>WXXX</c>
    /// frames), indistinguishable here from our own stamped annotation. <c>PlayoutFeeder</c> — the
    /// ONE production caller — gates this value through <c>IArtworkUrlEchoValidator</c> before ever
    /// storing it; no OTHER caller may treat this method's <c>ArtworkUrl</c> as pre-validated.
    /// </para>
    /// </summary>
    public (string? Title, string? Artist, double GainDb, string? ArtworkUrl) ExtractAnnotations()
    {
        var title = Values.TryGetValue("title", out var t) && t.Length > 0 ? t : (string?)null;
        var artist = Values.TryGetValue("artist", out var a) && a.Length > 0 ? a : (string?)null;
        var gainDb = ParseReplayGain();
        var artworkUrl = Values.TryGetValue("url", out var u) && u.Length > 0 ? u : (string?)null;
        return (title, artist, gainDb, artworkUrl);
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
