namespace GenWave.Core.Domain;

/// <summary>
/// Discriminated union expressing every outcome of an <see cref="Abstractions.IFontPackStore.UpsertAsync"/>
/// call (SPEC F104.5, STORY-282, PLAN T198; gh-#406 slice 2). Mirrors
/// <see cref="FontPackDeleteResult"/>'s own closed-hierarchy shape (a private base constructor, sealed
/// record cases) for the same exhaustive-switch guarantee. <see cref="FileCollision"/> is the
/// SANITIZED domain fact <c>FontPackRepository.UpsertAsync</c>'s own <c>catch</c> on a Postgres
/// unique-violation maps a raw storage exception into — this seam (and every caller upstream of it,
/// including <c>FontPackController</c>) never sees that exception, or its own internal detail text,
/// at all (F15.7, the L2 Postgres-confinement law's own repository-layer boundary — ARCHITECTURE.md
/// "Architecture governance").
/// </summary>
public abstract record FontPackUpsertResult
{
    private FontPackUpsertResult() { }

    /// <summary>The pack — and its whole face set — was written: a fresh install, or a re-install
    /// replacing the prior face set outright (SPEC F104 "Data model").</summary>
    public sealed record Upserted : FontPackUpsertResult;

    /// <summary>
    /// The write was refused because one of this pack's own face files is already installed under a
    /// DIFFERENT, already-installed pack — <c>station.font_pack_face.file</c> is UNIQUE across every
    /// installed pack, not scoped per-pack (db/32). <see cref="File"/>/<see cref="OwnerSlug"/> name the
    /// actual colliding file and its owning pack's slug, resolved by the repository's own post-failure
    /// re-read of <see cref="Abstractions.IFontPackStore.GetAllAsync"/> cross-referenced against the
    /// faces this write attempted — never the raw storage exception's own detail text (F15.7). Both
    /// are <see langword="null"/> together in the rare case that re-read does not cleanly resolve an
    /// owner (a colliding file can only ever belong to a different, already-installed pack, so this is
    /// a defensive fallback for an unexpected shape, never an ordinary outcome) — a caller falls back
    /// to generic wording for that case rather than trusting a partial identification.
    /// </summary>
    public sealed record FileCollision(string? File, string? OwnerSlug) : FontPackUpsertResult;
}
