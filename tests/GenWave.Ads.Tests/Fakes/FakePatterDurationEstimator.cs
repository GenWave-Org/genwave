using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// Scriptable <see cref="IPatterDurationEstimator"/> — answers a fixed <see cref="DurationPerCall"/>/
/// <see cref="Confidence"/> for every call and records the (Kind, PersonaName, Voice) tuple each one
/// arrived with (the <c>CapturingPatterDurationEstimator</c> shape GenWave.Orchestration.Tests already
/// uses for this exact interface). <see cref="Confidence"/> defaults to <see
/// cref="PatterEstimateConfidence.Heuristic"/> — the SAME tier the real
/// <c>RollingPatterDurationEstimator</c> always reports for <see cref="SegmentKind.Ad"/> today (no
/// caller observes a rendered ad's duration back into it yet), so a bare <c>new
/// FakePatterDurationEstimator()</c> is exactly the "constant-per-line stub" <see
/// cref="AdScriptValidator"/>'s duration check (PLAN T399 review F1) is built to distrust — its
/// <see cref="DurationPerCall"/> answer is IGNORED there in favor of the script's own text length. A
/// spec that needs to exercise the trusted-tier override path sets <see cref="Confidence"/> to
/// <see cref="PatterEstimateConfidence.Historical"/> or <see cref="PatterEstimateConfidence.Exact"/>.
/// </summary>
public sealed class FakePatterDurationEstimator : IPatterDurationEstimator
{
    public TimeSpan DurationPerCall { get; set; } = TimeSpan.FromSeconds(5);
    public PatterEstimateConfidence Confidence { get; set; } = PatterEstimateConfidence.Heuristic;
    public List<(SegmentKind Kind, string? PersonaName, string Voice)> Calls { get; } = [];

    public PatterDurationEstimate Estimate(SegmentKind kind, string? personaName, string voice)
    {
        Calls.Add((kind, personaName, voice));
        return new PatterDurationEstimate(DurationPerCall, Confidence);
    }

    public void ObserveRendered(SegmentKind kind, string? personaName, string voice, TimeSpan measured)
    {
    }
}
