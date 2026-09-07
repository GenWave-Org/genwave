namespace GenWave.Core.Domain;

/// <summary>
/// Discriminated union expressing every outcome of an
/// <see cref="Abstractions.IJinglePackStore.UpsertAsync"/> call (SPEC F165.2/F165.5, STORY-399,
/// PLAN T414). Mirrors <see cref="VoicePackUpsertResult"/>'s own closed-hierarchy shape (a private
/// base constructor, sealed record cases) for the same exhaustive-switch guarantee.
///
/// <para>
/// <b>Honest boundary — TWO connections, not one transaction.</b> <see cref="VoicePackUpsertResult"/>'s
/// own remarks describe a single-statement guard because <c>station.voice_pack</c> and
/// <c>station.voice_pack_voice</c> both live behind the SAME <c>station_svc</c> role. A jingle pack's
/// own reinstall guard cannot follow that shape: the rows it must drop-or-keep are
/// <c>library.media</c> rows (the <c>library_svc</c> role), while the reference it must check against
/// them — an active <c>station.ad_spot.bed_media_id</c> — lives behind <c>station_svc</c>, and no
/// cross-schema grant exists between the two (db/42's own header remarks: "the db/22 schema-role
/// boundary (station_svc has no grant into the library schema, library_svc none into station)";
/// db/45's own header remarks, verbatim, on this exact pair of columns: "`pack_slug` carries no
/// CHECK... it is not a REFERENCES constraint either, the same db/22 schema-role boundary rule that
/// already keeps show_id and bed_media_id/media_id as plain values"). One transaction spanning both
/// schemas is therefore not available to this seam at all — <c>JinglePackRepository</c> instead reads
/// the candidate rows to drop, checks them against <c>station.ad_spot</c> for an active reference,
/// and only once that read comes back clear does it write — STATION-first, with compensation:
/// <c>station.jingle_pack</c> lands first (its previous row, if any, saved beforehand), then the real
/// single-schema <c>library_svc</c> transaction for the drop-and-upsert; if THAT throws, the station
/// row is compensated back to whatever it held before this call (restored, or deleted if there was
/// none) before the original exception is rethrown, so the two sides of the schema boundary can never
/// durably disagree about which install is actually live. See that type's own
/// remarks for the exact read-then-write-then-compensate ordering and the same fail-soft race window
/// <see cref="VoicePackDeleteResult"/>'s own "Honest boundary" already accepts for a single-schema
/// guard.
/// </para>
/// </summary>
public abstract record JinglePackUpsertResult
{
    private JinglePackUpsertResult() { }

    /// <summary>The pack — and every one of the asset rows named in this install's own manifest —
    /// was written: a fresh install, or a re-install replacing the prior roster outright (SPEC F165.5
    /// "the id is preserved on re-install" upsert contract). <see cref="MediaIds"/> names every
    /// <c>library.media</c> row id this write touched, in the same order the caller's own asset list
    /// was supplied.</summary>
    public sealed record Upserted(IReadOnlyList<long> MediaIds) : JinglePackUpsertResult;

    /// <summary>
    /// The write was refused because a title this reinstall would otherwise DROP (present in the
    /// previously-installed pack, absent from the manifest now being installed) still sits behind an
    /// active <c>station.ad_spot.bed_media_id</c> reference — dropping it would leave that spot's
    /// background-music reference dangling. Nothing was written on either side of the schema boundary.
    /// <see cref="AdSpotIds"/> names every offending spot.
    /// </summary>
    public sealed record Refused(IReadOnlyList<long> AdSpotIds) : JinglePackUpsertResult;
}
