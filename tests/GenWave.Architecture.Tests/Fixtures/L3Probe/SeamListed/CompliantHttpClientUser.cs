// Fixture type for STORY-291 AC4's self-exercising negative probe (Story291_ConventionLaws.cs).
// Never wired into any DI container or call path — exists only so the probe can prove L3's
// ArchUnitNET-based detector actually discriminates a seam-listed HttpClient construction from a
// stray one, without editing production code or coupling the proof to production's own seam list.

namespace GenWave.Architecture.Tests.Fixtures.L3Probe.SeamListed;

/// <summary>Stands in for a real designated client seam (e.g. KokoroTtsSynthesizer) — the probe's
/// own seam-listed provider allows exactly this one type, the way the real L3 rule allows only
/// <see cref="GenWave.Architecture.Tests.Support.HttpClientSeams.DesignatedSeams"/>'s types, so this
/// type is allowed to construct an HttpClient.</summary>
public sealed class CompliantHttpClientUser
{
    public HttpClient Client { get; } = new HttpClient();
}
