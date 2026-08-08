// Fixture type for STORY-292 AC2's self-exercising negative probe (Story292_HostTripwire.cs).
// Never wired into any DI container or call path — proves the compiler-generated roll-up the T214
// design notes call out: an async lambda's closure/state-machine types, however deeply nested, still
// attribute to this declaring type when they land under a reserved namespace.

namespace GenWave.Architecture.Tests.Fixtures.L5Probe.ReservedHit;

/// <summary>Declares an async lambda whose closure and state-machine types compile to nested types
/// under this same reserved stand-in namespace — mirrors <c>HttpClientSeams</c>'s L3 async-lambda
/// review probe (F1), here proving <see cref="GenWave.Architecture.Tests.Support.HostNamespaceTripwire"/>
/// rolls those compiler-generated types up to THIS type instead of reporting an unreadable
/// compiler-generated name, or missing them entirely.</summary>
public sealed class AsyncLambdaClosure
{
    public async Task<int> RunAsync()
    {
        var offset = 1;
        Func<Task<int>> closure = async () =>
        {
            await Task.Delay(1);
            return offset + 1;
        };

        return await closure();
    }
}
