namespace GenWave.Host.Api;

/// <summary>One row of <c>GET /api/booth-log</c> (SPEC F72.2): an operator-readable narrative entry.</summary>
public sealed record BoothLogEntryDto(DateTime OccurredAt, string Kind, string Summary);
