namespace GenWave.Core.Domain;

/// <summary>
/// One <c>library.media</c> row unscoped from a deleted show (SPEC F115.4, STORY-305, PLAN T240) —
/// what <see cref="Abstractions.IShowImagingScope.UnscopeAsync"/> both names and clears. The show
/// delete guard's response names these rows so an operator can see exactly what branding just went
/// station-wide again. <see cref="Title"/> is null exactly when the row's own tag is (enrichment may
/// not have run yet) — a caller names the row by <see cref="MediaId"/> in that case.
/// </summary>
public sealed record ScopedImagingRow(long MediaId, string? Title);
