namespace GenWave.Host.Api;

/// <summary>
/// One <c>library.media</c> row the show delete guard named and unscoped (SPEC F115.4). Mirrors
/// <see cref="Core.Domain.ScopedImagingRow"/> field-for-field — kept as its own wire type anyway
/// (house convention: a domain record never serializes directly across the wire, even when its own
/// field names already happen to match the shape the response wants).
/// </summary>
public sealed record ScopedImagingRowDto(long MediaId, string? Title);
