// Fixture type for STORY-291 AC4's self-exercising negative probe (Story291_ConventionLaws.cs).
// Never wired into any DI container or call path.

namespace GenWave.Architecture.Tests.Fixtures.L3Probe.Elsewhere;

/// <summary>Outside the fixture's seam list and constructs an HttpClient — the one type the probe
/// expects to fail, standing in for a stray <c>new HttpClient()</c> outside the real seam list.</summary>
public sealed class ViolatesSeamConfinement
{
    public HttpClient Client { get; } = new HttpClient();
}
