// Fixture type for STORY-291 AC4's self-exercising negative probe (Story291_ConventionLaws.cs).
// Never wired into any DI container or call path — stands in for the exact review finding (F4): a
// working outbound surface built entirely from the handler family, never naming HttpClient itself.

namespace GenWave.Architecture.Tests.Fixtures.L3Probe.Elsewhere;

/// <summary>Outside the fixture's seam list; constructs a <c>SocketsHttpHandler</c> with no
/// <c>HttpClient</c> anywhere in sight — proves the widened <see cref="GenWave.Architecture.Tests.Support.HttpClientSeams.ForbiddenTypes"/>
/// family catches the handler alone, not just the convenience type.</summary>
public sealed class HandlerOnlyConstruction
{
    public SocketsHttpHandler Handler { get; } = new SocketsHttpHandler();
}
