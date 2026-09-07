using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F165.2/F165.5/F165.6; STORY-399/401; PLAN T414) — persistence for jingle packs
/// installed from the Community Catalog's <c>jingle-pack</c> kind. Physically in
/// <c>GenWave.Core</c>, NOT the published <c>GenWave.Abstractions</c> NuGet, for the same reason
/// <see cref="IVoicePackStore"/> is not: this seam has no consumer outside this solution. <c>definition</c>
/// stays opaque jsonb text here — this project knows nothing of
/// <c>GenWave.Host.Catalog.CatalogJinglePackManifest</c> (downstream of this seam), so the controller
/// (de)serializes at its own edge, mirroring <see cref="IVoicePackStore"/>'s own discipline.
///
/// <para>
/// <b>Unlike <see cref="IVoicePackStore"/>, this store's own asset rows are <c>library.media</c> rows,
/// not a station-schema child table</b> — db/45's own header remarks: "There is deliberately NO
/// <c>_asset</c> table: the assets ARE library rows, and the ad worker's bed-pool query (F168.1) is a
/// single-schema read against the same pool the imaging-kind rotation fence already governs
/// (F158.4)". That choice is what makes this seam's own <see cref="UpsertAsync"/>/
/// <see cref="DeleteAsync"/> a TWO-CONNECTION guard-read-then-write rather than
/// <see cref="IVoicePackStore"/>'s own single in-statement guard — see
/// <see cref="JinglePackUpsertResult"/>'s and <see cref="JinglePackDeleteResult"/>'s own "Honest
/// boundary" remarks for the full reasoning, citing db/42's and db/45's own header text for the
/// underlying db/22 schema-role boundary (<c>station_svc</c> has no grant into the library schema,
/// <c>library_svc</c> none into station) that rules out a single cross-schema statement entirely.
/// </para>
/// </summary>
public interface IJinglePackStore
{
    /// <summary>
    /// Every engine-visible <c>library.media.path</c> currently installed under this exact
    /// <paramref name="slug"/> (empty if no such pack is installed yet). A REINSTALL calls this
    /// BEFORE its own <see cref="UpsertAsync"/>, then diffs the result against what it actually wrote
    /// this attempt: a title present in the previous install but absent from the new manifest already
    /// drops its OWN row (<see cref="UpsertAsync"/>'s own job) but leaves its file behind on disk
    /// unless the controller unlinks it after a successful upsert — this is that diff's data source.
    /// Distinct from <see cref="IVoicePackStore.FindVoiceIdsForPackAsync"/>'s own unwind-scoped use:
    /// this store's reinstall-time FAILURE unwind is <c>JinglePackController.UnwindWritten</c>'s own
    /// job, a purely filesystem-level "what did THIS attempt displace" mechanism that needs no
    /// database read at all — this method instead answers what the PREVIOUS, already-committed
    /// install owned, independent of anything the current attempt itself touched.
    /// </summary>
    Task<IReadOnlyList<string>> FindPathsForPackAsync(string slug, CancellationToken ct);

    /// <summary>
    /// Upserts a whole pack (by <paramref name="slug"/>) AND its full asset roster (SPEC F165.5):
    /// a re-install of an already-installed slug replaces <paramref name="definition"/>/
    /// <paramref name="importedFrom"/> on the pack row, and per <paramref name="assets"/> entry
    /// either inserts a fresh <c>library.media</c> row or, keyed on
    /// <c>(pack_slug, title)</c> (db/45's own <c>library.media_pack_slug_title_key</c>), updates the
    /// existing one IN PLACE — the row's own <c>id</c> survives a reinstall (F165.5) rather than being
    /// dropped and recreated, so nothing else that may already reference that id (an
    /// <c>ad_spot.bed_media_id</c>) is silently invalidated by an ordinary reinstall. A title
    /// installed before but ABSENT from <paramref name="assets"/> now is dropped — UNLESS an active
    /// <c>station.ad_spot.bed_media_id</c> still names it, in which case the whole write is refused
    /// (<see cref="JinglePackUpsertResult.Refused"/>) before either side of the schema boundary is
    /// touched. See this interface's own "Honest boundary" remarks for the two-connection ordering
    /// this guard actually runs under.
    /// </summary>
    Task<JinglePackUpsertResult> UpsertAsync(
        string slug, string definition, string importedFrom,
        IReadOnlyList<JinglePackAssetInput> assets, CancellationToken ct);

    /// <summary>
    /// Removes an installed pack by <paramref name="slug"/> (SPEC F165.6, STORY-401) — refused, naming
    /// every referencing ad spot, while any of its asset rows still sits behind an active
    /// <c>station.ad_spot.bed_media_id</c>; with none, the pack row and every one of its
    /// <c>library.media</c> asset rows are removed. Unlike <see cref="IVoicePackStore.DeleteAsync"/>'s
    /// own single in-statement guard, this is a guard-READ against <c>station.ad_spot</c> followed by
    /// two single-schema deletes — see this interface's own "Honest boundary" remarks.
    /// </summary>
    Task<JinglePackDeleteResult> DeleteAsync(string slug, CancellationToken ct);
}
