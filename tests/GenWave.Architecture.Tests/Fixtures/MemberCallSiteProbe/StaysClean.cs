// Fixture type for T277 review's self-exercising probe (Story323_FitnessLawsHoldTheSeamsShut.cs).
// Never wired into any DI container or call path.

namespace GenWave.Architecture.Tests.Fixtures.MemberCallSiteProbe;

/// <summary>Calls nothing forbidden at all — the baseline "no false positive" fixture every other
/// law's probe folder also carries (mirrors L3Probe's own <c>Elsewhere/StaysClean.cs</c>).</summary>
public sealed class StaysClean
{
    public int AddOne(int value) => value + 1;
}
