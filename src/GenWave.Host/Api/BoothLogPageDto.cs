namespace GenWave.Host.Api;

/// <summary>
/// Response shape for <c>GET /api/booth-log</c> (SPEC F72.2): a newest-first keyset page.
/// <see cref="NextBefore"/> is <see langword="null"/> when <see cref="Entries"/> is the oldest page —
/// pass it back as the next request's <c>?before=</c> to keep paging.
/// </summary>
public sealed record BoothLogPageDto(IReadOnlyList<BoothLogEntryDto> Entries, string? NextBefore);
