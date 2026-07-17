using GenWave.Core.Domain;

namespace GenWave.Core.Playout;

/// <summary>Per-track playout gain for level matching (PRD §4.2).</summary>
public static class Gain
{
    /// <summary>
    /// dB of gain to apply so the track plays back at <paramref name="targetLufs"/>, clamped so that
    /// a gained-up true peak stays under <paramref name="ceilingDbtp"/>. The allowed gain is the
    /// smaller of "what target wants" and "what headroom allows" — loud-peaked tracks simply won't
    /// reach target, which is correct clip-avoiding behaviour. Unmeasurable (silent/gated) tracks get
    /// zero: they are never auto-amplified (PRD §4.1).
    /// </summary>
    public static double NormGainDb(in Loudness l, double targetLufs = -16.0, double ceilingDbtp = -1.0)
        => l.Measurable ? Math.Min(targetLufs - l.IntegratedLufs, ceilingDbtp - l.TruePeakDbtp) : 0.0;
}
