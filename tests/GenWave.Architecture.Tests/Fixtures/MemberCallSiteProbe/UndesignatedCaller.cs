// Fixture type for T277 review's self-exercising probe (Story323_FitnessLawsHoldTheSeamsShut.cs).
// Never wired into any DI container or call path.

namespace GenWave.Architecture.Tests.Fixtures.MemberCallSiteProbe;

using GenWave.Core.Abstractions;

/// <summary>A caller shaped exactly like <see cref="DesignatedRelayLike"/> but ABSENT from the probe's
/// exemption list — proves an ordinary, unlisted caller of the same forbidden overload is still
/// caught, not merely that a listed one is excused.</summary>
public sealed class UndesignatedCaller
{
    public Task<string> CallThePlainOverload(ITtsSynthesizer synth, string text, string voice, CancellationToken ct) =>
        synth.SynthesizeAsync(text, voice, ct);
}
