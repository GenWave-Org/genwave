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
public sealed record ShowDraft(string Name, string? Tagline = null, string? Flavor = null);
