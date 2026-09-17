using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.TestSupport.Fakes;

/// <summary>
/// gh-#463 — records every <see cref="Estimate"/> call's (Kind, PersonaName, Voice, ShowName) tuple so
/// a spec can assert exactly what a caller (BuildBoundaryFit) hands the estimator, without caring what
/// it does with the answer. Always answers at <see cref="PatterEstimateConfidence.Heuristic"/> — no
/// spec using this fake asserts on the returned duration itself, only the call shape.
/// </summary>
public sealed class CapturingPatterDurationEstimator : IPatterDurationEstimator
{
    /// <summary>Every <see cref="Estimate(SegmentKind,string?,string,string?)"/> call, in call order.</summary>
    public List<(SegmentKind Kind, string? PersonaName, string Voice, string? ShowName)> Calls { get; } = [];

    /// <summary>Records the call (showName: null) and answers the fixed heuristic estimate.</summary>
    public PatterDurationEstimate Estimate(SegmentKind kind, string? personaName, string voice) =>
        Estimate(kind, personaName, voice, showName: null);

    /// <summary>Records the call and answers the fixed heuristic estimate.</summary>
    public PatterDurationEstimate Estimate(SegmentKind kind, string? personaName, string voice, string? showName)
    {
        Calls.Add((kind, personaName, voice, showName));
        return new PatterDurationEstimate(TimeSpan.FromSeconds(5), PatterEstimateConfidence.Heuristic);
    }

    /// <summary>No-op — this fake never simulates historical-tier learning.</summary>
    public void ObserveRendered(SegmentKind kind, string? personaName, string voice, TimeSpan measured)
    {
    }
}
