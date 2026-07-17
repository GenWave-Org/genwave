namespace GenWave.Core.Domain;

/// <summary>
/// Bit-field selecting which enrichment columns to sentinel-reset before re-queueing a track.
/// (STORY-050, Epic J — SPEC F20.10.)
///
/// <para>
/// <b>AC4 note:</b> Passing <see cref="None"/> to <c>IAdminMediaReenrichment</c> is not a
/// valid call — callers MUST normalize <see cref="None"/> to <see cref="All"/> before
/// invoking either schedule method. The endpoint layer (L6) is responsible for this
/// normalization; the contract itself does not silently no-op a <see cref="None"/> request.
/// </para>
/// </summary>
[Flags]
public enum ReenrichFields
{
    /// <summary>
    /// No fields selected. Must not be passed to <c>IAdminMediaReenrichment</c> — normalize to
    /// <see cref="All"/> at the endpoint layer (L6) before calling.
    /// </summary>
    None = 0,

    /// <summary>
    /// Reset cue-point columns (<c>cue_in_sec</c>, <c>cue_out_sec</c>, <c>cue_analyzed_at</c>)
    /// to null; the row stays <c>ready</c> and selectable; the F13 backfill predicate
    /// (<c>state='ready' AND cue_analyzed_at IS NULL</c>) reclaims it (SPEC F20.10).
    /// </summary>
    Cue = 1,

    /// <summary>
    /// Reset energy columns (<c>intro_energy</c>, <c>outro_energy</c>, <c>energy_analyzed_at</c>)
    /// to null; the row stays <c>ready</c> and selectable; the F17.9 backfill predicate
    /// (<c>state='ready' AND energy_analyzed_at IS NULL</c>) reclaims it (SPEC F20.10).
    /// </summary>
    Energy = 2,

    /// <summary>
    /// Reset loudness columns and set <c>state = discovered</c> so the loudness analyzer
    /// re-measures the track on next enrichment pass (SPEC F20.10).
    /// </summary>
    Loudness = 4,

    /// <summary>
    /// Reset <c>tags_edited_at</c> and set <c>state = discovered</c> so the tag scanner
    /// re-reads the track's embedded metadata on next enrichment pass (SPEC F20.10).
    /// </summary>
    Tags = 8,

    /// <summary>
    /// Reset BPM columns (<c>bpm</c>, <c>bpm_analyzed_at</c>) to null; the row stays <c>ready</c>
    /// and selectable; the F46.3 backfill predicate (<c>state='ready' AND bpm_analyzed_at IS NULL</c>)
    /// reclaims it (SPEC F46.4).
    /// </summary>
    Bpm = 16,

    /// <summary>
    /// Reset the <c>year_lookup_at</c> sentinel ONLY — unlike every sibling flag, the underlying
    /// value (<c>year</c>) is left untouched. Retrying the MusicBrainz lookup (e.g. after a since-
    /// fixed endpoint outage) must never clobber a year that was already written; a *wrong* year's
    /// correction surface is the F18 PATCH, not this reset. The row stays <c>ready</c> and selectable;
    /// the F48.3 backfill predicate (<c>state='ready' AND year IS NULL AND year_lookup_at IS NULL</c>)
    /// reclaims it — but only fires a fresh lookup when <c>year</c> is ALSO still null, so this reset
    /// is a no-op for a row that already has a year (SPEC F48.6).
    /// </summary>
    Year = 32,

    /// <summary>All six enrichment fields — the union of <see cref="Cue"/>, <see cref="Energy"/>,
    /// <see cref="Loudness"/>, <see cref="Tags"/>, <see cref="Bpm"/>, and <see cref="Year"/>.</summary>
    All = Cue | Energy | Loudness | Tags | Bpm | Year,
}
