namespace GenWave.Abstractions.Playout;

/// <summary>
/// The energy band a <see cref="SegmentEnvelope"/> admits, expressed in the same percentile space
/// as <c>library.media.energy</c> (SPEC F80.1) — 0 is the least-energetic ready track in the
/// library, 1 the most. Bounds are enforced sane at construction: <c>0 &lt;= Min &lt;= Max &lt;= 1</c>.
/// </summary>
public sealed record EnergyRange(double Min, double Max)
{
    /// <summary>The lower bound of the admitted energy band, in <c>[0, 1]</c>.</summary>
    public double Min { get; init; } = Min is >= 0.0 and <= 1.0
        ? Min
        : throw new ArgumentOutOfRangeException(nameof(Min), Min, "Min must be within [0, 1].");

    /// <summary>The upper bound of the admitted energy band, in <c>[Min, 1]</c>.</summary>
    public double Max { get; init; } = Max is >= 0.0 and <= 1.0 && Max >= Min
        ? Max
        : throw new ArgumentOutOfRangeException(nameof(Max), Max, $"Max must be within [{Min}, 1].");

    /// <summary>The full [0,1] band — no energy constraint (the station-default fallback, SPEC F81.3).</summary>
    public static EnergyRange Unconstrained { get; } = new(0.0, 1.0);
}
