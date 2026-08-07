namespace GenWave.Core.Domain;

/// <summary>
/// Discriminated union expressing every outcome of an <see cref="Abstractions.IFontPackStore.DeleteAsync"/>
/// call (SPEC F104.14, STORY-288, PLAN T208) — mirrors <see cref="PersonaWriteResult"/>'s own
/// closed-hierarchy shape (a private base constructor, sealed record cases) for the same
/// exhaustive-switch guarantee. Unlike <see cref="PersonaWriteResult.ScheduledElsewhere"/>'s FK-backed
/// guard (a real <c>ON DELETE RESTRICT</c> constraint the store merely REPORTS), the reference this type
/// guards against — a saved/imported <c>station.theme</c> row naming one of this pack's own faces —
/// lives inside an opaque jsonb blob no foreign key can express: SPEC F104.14's "the persona-delete
/// FK-guard precedent" applied where there is no literal FK to lean on. See
/// <see cref="Abstractions.IFontPackStore.DeleteAsync"/>'s own remarks for how the guard is enforced —
/// atomically, inside the delete statement itself, never as an advisory check-then-delete spanning two
/// separate round trips — and for the honest READ COMMITTED boundary that single-statement shape does
/// and does not close.
/// </summary>
public abstract record FontPackDeleteResult
{
    private FontPackDeleteResult() { }

    /// <summary>The pack — and, by <c>station.font_pack_face</c>'s own <c>ON DELETE CASCADE</c>, every
    /// one of its faces — was removed.</summary>
    public sealed record Deleted : FontPackDeleteResult;

    /// <summary>No installed pack with the requested slug exists.</summary>
    public sealed record NotFound : FontPackDeleteResult;

    /// <summary>
    /// The delete was refused because at least one <c>station.theme</c> row still references one of
    /// this pack's own faces. <see cref="ThemeSlugs"/> names every offending theme, byte-ordinal-sorted
    /// (<c>FontPackRepository.DeleteAsync</c>'s own naming query orders <c>collate "C"</c>, deliberately
    /// — Postgres's own default collation is whatever the cluster was initialised with, not necessarily
    /// byte-ordinal, review finding N5) — the persona-delete precedent's own "name every offending row"
    /// contract, applied here to owner
    /// themes rather than schedule slots. Usually non-empty; MAY rarely be empty if a race between the
    /// delete's own refusal and the store's follow-up naming re-query closes the other way (every
    /// referencing theme edited/removed in that narrow window) — mirrors
    /// <see cref="PersonaWriteResult.ScheduledElsewhere"/>'s own "may be empty if the race closed the
    /// other way" remarks; a caller falls back to generic wording for that case rather than an empty
    /// theme list.
    /// </summary>
    public sealed record Referenced(IReadOnlyList<string> ThemeSlugs) : FontPackDeleteResult;
}
