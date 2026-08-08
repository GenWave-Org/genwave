// Fixture type for STORY-291 AC4's self-exercising negative probe (Story291_ConventionLaws.cs).
// Never wired into any DI container or call path — stands in for the exact review finding (F1):
// a minimal-API async lambda handler (MediaEndpoints.cs's shape) constructing HttpClient directly.

namespace GenWave.Architecture.Tests.Fixtures.L3Probe.Elsewhere;

/// <summary>Outside the fixture's seam list; constructs an HttpClient from inside an async lambda —
/// the exact shape ArchUnitNET's type graph never saw (its compiler-generated state machine nests
/// inside another compiler-generated closure type), proving <see cref="GenWave.Architecture.Tests.Support.HttpClientMetadataScan"/>
/// closes that hole.</summary>
public static class AsyncLambdaConstructsClient
{
    public static Func<Task> MakeHandler() => async () =>
    {
        using var stray = new HttpClient();
        await Task.Yield();
    };
}
