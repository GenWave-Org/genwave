// Fixture type for T277 review's self-exercising probe (Story323_FitnessLawsHoldTheSeamsShut.cs).
// Never wired into any DI container or call path.

namespace GenWave.Architecture.Tests.Fixtures.MemberCallSiteProbe;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>Calls only the ALLOWED context-aware overload (2 parameters) — never the forbidden plain
/// overload (3 parameters) that shares its name — proving
/// <see cref="ForbiddenMemberSignature.ParameterCount"/> disambiguation still holds and this near-miss
/// is never mistaken for a violation.</summary>
public sealed class CallsOnlyTheContextOverload
{
    public Task<string> CallTheContextOverload(ITtsSynthesizer synth, TtsRenderContext context, CancellationToken ct) =>
        synth.SynthesizeAsync(context, ct);
}
