namespace GenWave.Core.Domain;

/// <summary>
/// Caller-supplied fields for authoring or editing a <see cref="Show"/> (SPEC F115.1, STORY-305, PLAN
/// T239). Always an AUTHORED write —
/// <see cref="Abstractions.IShowStore.CreateAsync"/>/<see cref="Abstractions.IShowStore.UpdateAsync"/>
/// never set <c>imported_from</c>/<c>imported_at</c> from this draft, mirroring
/// <see cref="PersonaDraft"/>'s own posture; the import write path is a separate, later seam (PLAN
/// T254).
/// </summary>
/// <param name="Tagline">SPEC F115.1's ≤120-char budget (<see cref="ShowBudgets.TaglineMaxChars"/>) —
/// checked at the write seam, not this record. <c>null</c>/empty means no tagline.</param>
/// <param name="Flavor">SPEC F115.1's ≤400-char budget (<see cref="ShowBudgets.FlavorMaxChars"/>) —
/// checked at the write seam, not this record. <c>null</c>/empty means no flavor.</param>
/// <param name="SponsorId">SPEC F175.1 (STORY-430, PLAN T449) — a full replace, the same posture as
/// <paramref name="Tagline"/>/<paramref name="Flavor"/>: <see langword="null"/> (the default, whether
/// because the caller omitted the field or sent it explicitly) clears any sponsor the show currently
/// carries. The caller (<c>ShowsController</c>) resolves a non-null id against
/// <c>ISponsorStore.GetAsync</c> BEFORE this draft is ever built, so the write seam never has to turn a
/// dangling foreign key into an error.</param>
public sealed record ShowDraft(string Name, string? Tagline = null, string? Flavor = null, long? SponsorId = null);
