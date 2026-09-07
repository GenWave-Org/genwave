namespace GenWave.Core.Domain;

/// <summary>
/// Discriminated union expressing every outcome of an
/// <see cref="Abstractions.IVoicePackStore.UpsertAsync"/> call (SPEC F164.5/F166.4, STORY-395/398,
/// PLAN T413). Mirrors <see cref="FontPackUpsertResult"/>'s own closed-hierarchy shape (a private base
/// constructor, sealed record cases) for the same exhaustive-switch guarantee.
/// <see cref="VoiceIdCollision"/> is the belt-and-suspenders backstop behind
/// <see cref="Abstractions.IVoicePackStore.FindInstalledVoiceIdsAsync"/>'s own advisory pre-check: the
/// pre-check closes the ordinary case fast (before any file is written), this closes the race a
/// concurrent install could still land inside, by mapping the repository's own
/// <c>voice_pack_voice_voice_id_uk</c> unique-violation into a domain fact — the caller never sees the
/// raw Postgres exception or its own detail text (F15.7, the L2 Postgres-confinement law).
/// </summary>
public abstract record VoicePackUpsertResult
{
    private VoicePackUpsertResult() { }

    /// <summary>The pack — and its whole voice roster — was written: a fresh install, or a re-install
    /// replacing the prior roster outright (SPEC F164.5 "Data model").</summary>
    public sealed record Upserted : VoicePackUpsertResult;

    /// <summary>
    /// The write was refused because one of this pack's own voice ids is already installed under a
    /// DIFFERENT, already-installed pack — <c>voice_pack_voice_voice_id_uk</c> is UNIQUE across every
    /// installed pack (SPEC F166.4). <see cref="VoiceId"/>/<see cref="OwnerSlug"/> name the actual
    /// colliding id and its owning pack's slug, resolved by the repository's own post-failure re-read
    /// cross-referenced against the voices this write attempted. Both are <see langword="null"/>
    /// together in the rare case that re-read does not cleanly resolve an owner (a defensive fallback
    /// for an unexpected shape, never an ordinary outcome) — a caller falls back to generic wording for
    /// that case rather than trusting a partial identification.
    /// </summary>
    public sealed record VoiceIdCollision(string? VoiceId, string? OwnerSlug) : VoicePackUpsertResult;
}
