// Fixture type for T277 review's self-exercising probe (Story323_FitnessLawsHoldTheSeamsShut.cs).
// Never wired into any DI container or call path — exists only so the probe can prove
// MemberCallSiteScan's exemption filter actually SUPPRESSES a real hit when a caller is named in the
// exemption list, not merely that the underlying scan can find one (T277 review finding 1: corrupting
// both L7 relay names left the suite green before this fixture existed, because no real call site
// exercised the filter at all).

namespace GenWave.Architecture.Tests.Fixtures.MemberCallSiteProbe;

using GenWave.Core.Abstractions;

/// <summary>Stand-in for a designated TYPE-level exemption (L7's relay shape): calls the plain,
/// context-less overload directly, on purpose. The probe's own exemption list names this exact type,
/// so this call must never appear among the probe's violations.</summary>
public sealed class DesignatedRelayLike
{
    public Task<string> CallThePlainOverload(ITtsSynthesizer synth, string text, string voice, CancellationToken ct) =>
        synth.SynthesizeAsync(text, voice, ct);
}
