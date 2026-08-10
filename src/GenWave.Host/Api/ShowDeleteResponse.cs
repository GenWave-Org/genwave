namespace GenWave.Host.Api;

/// <summary>
/// Body for a successful <c>DELETE /api/shows/{slug}</c> that unscoped one or more show-scoped
/// imaging rows (SPEC F115.4) — present only on that path; a delete that unscoped nothing answers
/// plain <c>204 No Content</c> instead (nothing to name).
/// </summary>
public sealed record ShowDeleteResponse(IReadOnlyList<ScopedImagingRowDto> UnscopedImaging);
