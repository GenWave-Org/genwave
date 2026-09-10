namespace GenWave.Host.Tests.Support;

/// <summary>
/// One <c>station.ad_spot</c> row, read straight off Postgres (STORY-425; PLAN T445) —
/// <see cref="AdSpotJobTestHelpers.ReadAdSpotRowAsync"/>'s own return shape. Carries only the columns
/// STORY-425's own facts assert on directly against the DATABASE (never merely what
/// <see cref="GenWave.Host.Api.AdsController"/> echoes back over the wire) — the row a promotion
/// actually left behind, not the response body a client happened to read.
/// </summary>
internal sealed record AdSpotRow(
    string State, long? MediaId, string? PreviewPath, string? PreviewKey, DateTime? PreviewAt);
