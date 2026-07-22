using GenWave.Abstractions.Playout;

namespace GenWave.Orchestration;

/// <summary>
/// SPEC F82.2 — positions a persona's <c>energyDisposition</c> inside an envelope's
/// <see cref="EnergyRange"/>: <c>target = clamp(mid + disposition·half, min, max)</c>, where
/// <c>mid</c>/<c>half</c> are the range's midpoint and half-width. Same law (the envelope's own
/// range), different feel per persona — a disposition of -1 targets the range's floor, +1 its
/// ceiling, 0 its midpoint, and the final clamp holds even for an out-of-range disposition (a
/// <c>PersonaCard.EnergyDisposition</c> is not itself bounded at construction — see its own remarks —
/// so this is where that law is actually enforced).
/// </summary>
public static class EnergyTarget
{
    public static double Compute(EnergyRange range, double disposition)
    {
        var mid = (range.Min + range.Max) / 2.0;
        var half = (range.Max - range.Min) / 2.0;
        return Math.Clamp(mid + disposition * half, range.Min, range.Max);
    }
}
