using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>An estimator that answers the same duration and confidence for every line.</summary>
internal sealed class FixedDurationEstimator(TimeSpan duration, PatterEstimateConfidence confidence) : IPatterDurationEstimator
{
    public PatterDurationEstimate Estimate(SegmentKind kind, string? personaName, string voice) => new(duration, confidence);

    public void ObserveRendered(SegmentKind kind, string? personaName, string voice, TimeSpan measured)
    {
    }
}
