namespace GenWave.Core.Domain;

/// <summary>
/// Discriminated union expressing every outcome of an
/// <see cref="Abstractions.IVoicePackStore.DeleteAsync"/> call (SPEC F164.6, STORY-401, PLAN T413) —
/// mirrors <see cref="FontPackDeleteResult"/>'s own closed-hierarchy shape (a private base constructor,
/// sealed record cases) for the same exhaustive-switch guarantee. Unlike
/// <see cref="FontPackDeleteResult.Referenced"/>'s single opaque-jsonb substring guard, a voice pack is
/// guarded by TWO independent references — an active <c>station.ad_spot.voice_plan</c> entry naming one
/// of the pack's voice ids, and a <c>station.persona.voice</c> column naming one directly — both
/// enforced inside the SAME <c>delete ... where not exists (...) and not exists (...)</c> statement
/// (see <c>VoicePackRepository.DeleteAsync</c>'s own remarks for the exact SQL and its honest READ
/// COMMITTED boundary).
/// </summary>
public abstract record VoicePackDeleteResult
{
    private VoicePackDeleteResult() { }

    /// <summary>The pack — and, by <c>station.voice_pack_voice</c>'s own <c>ON DELETE CASCADE</c>, every
    /// one of its voice rows — was removed. <see cref="Files"/> names every relative <c>.pt</c> file the
    /// deleted rows carried, read BEFORE the delete in the same transaction (the cascade removes the
    /// rows that would otherwise let a caller name them afterward) — the controller unlinks each one
    /// from <c>Packs:VoicesRoot</c> once this result is returned.</summary>
    public sealed record Deleted(IReadOnlyList<string> Files) : VoicePackDeleteResult;

    /// <summary>No installed pack with the requested slug exists.</summary>
    public sealed record NotFound : VoicePackDeleteResult;

    /// <summary>
    /// The delete was refused because at least one active ad spot's voice plan, or at least one
    /// persona, still names one of this pack's voice ids. <see cref="AdSpotIds"/> and
    /// <see cref="PersonaNames"/> (byte-ordinal <c>collate "C"</c> sorted — Postgres's own default
    /// collation is whatever the cluster was initialised with, not necessarily byte-ordinal) name every
    /// offending reference, re-queried immediately after the delete's own refusal purely to report them
    /// — mirrors <see cref="FontPackDeleteResult.Referenced"/>'s own "may rarely be empty if the race
    /// closed the other way" remarks; a caller falls back to generic wording for that case rather than
    /// an empty list.
    /// </summary>
    public sealed record Referenced(IReadOnlyList<long> AdSpotIds, IReadOnlyList<string> PersonaNames) : VoicePackDeleteResult;
}
