namespace GenWave.Core.Domain;

/// <summary>
/// One distinct, case-insensitively-grouped value of a <see cref="FacetField"/> (SPEC F52.1) —
/// the result shape of <see cref="Abstractions.IMediaCatalog.GetFacetsAsync"/>.
/// <para>
/// <see cref="Value"/> is a representative original casing for the group (e.g. "Rock" and "rock"
/// group together; one of the two casings is picked deterministically — see the repository's
/// implementation note). <see cref="Count"/> is the group's total row count, not a per-casing-variant
/// count, so it truthfully answers "how many rows would this exact filter touch."
/// </para>
/// </summary>
public sealed record FacetValue(string Value, int Count);
