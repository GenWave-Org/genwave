// Fixture type for STORY-291 AC4's self-exercising negative probe (Story291_ConventionLaws.cs).
// Never wired into any DI container or call path — stands in for the exact review finding (F4):
// new HttpMessageInvoker(new SocketsHttpHandler()) is a fully working outbound client that never
// touches System.Net.Http.HttpClient at all.

namespace GenWave.Architecture.Tests.Fixtures.L3Probe.Elsewhere;

/// <summary>Outside the fixture's seam list; builds an <c>HttpMessageInvoker</c> directly over a
/// <c>SocketsHttpHandler</c> — a real, working outbound client an <c>HttpClient</c>-only forbid would
/// green-light.</summary>
public sealed class InvokerOnlyConstruction
{
    public HttpMessageInvoker Invoker { get; } = new HttpMessageInvoker(new SocketsHttpHandler());
}
