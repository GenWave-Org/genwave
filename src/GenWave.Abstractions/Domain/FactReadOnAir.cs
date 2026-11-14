namespace GenWave.Core.Domain;

/// <summary>
/// A context fact the accepted copy actually used (SPEC F218.15): the writer's truth gate found the fact's
/// claims in the copy. Rides <see cref="SegmentCopy.FactRead"/> to <see cref="Events.SegmentGenerated.FactRead"/>.
/// </summary>
/// <param name="Key">The originating context provider's key (e.g. <c>weather</c>).</param>
/// <param name="Text">The fact text, as the provider produced it.</param>
public sealed record FactReadOnAir(string Key, string Text);
