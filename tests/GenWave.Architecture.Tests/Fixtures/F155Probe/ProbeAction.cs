// Fixture type for STORY-380 AC4's self-exercising negative probe (Story380_GardenerNamespaceAndDisjointness.cs,
// PLAN T367 review HIGH-1/MED-1). Never wired into any DI container or call path.

namespace GenWave.Architecture.Tests.Fixtures.F155Probe;

/// <summary>
/// Two entry points, each proving one T367 review finding against
/// <see cref="GenWave.Architecture.Tests.Support.GardenerThumbDisjointnessScan"/> directly (never
/// through the real production actions):
///
/// <list type="bullet">
/// <item><description><see cref="ReachesForbiddenViaLambdaAndLocalFunction"/> (HIGH-1) — the exact
/// review reproduction shape (<c>await Task.Run(async () =&gt; await accrual.ThumbAsync(...))</c>
/// inside <c>BoothLogController.ThumbStation</c> passed the OLD name-prefix redirect outright) —
/// reaches <see cref="ForbiddenRepository.Touch"/> once through an async LAMBDA and once through an
/// async LOCAL FUNCTION, neither named <c>&lt;MethodName&gt;d__N</c> the way a plain async method's
/// own state machine is.</description></item>
/// <item><description><see cref="ProbeEntryForOverload"/> (MED-1) — calls the SECOND of two async
/// overloads sharing the name <c>Overload</c> (declared in that order below); the FIRST is harmless.
/// The old prefix search matched whichever nested <c>&lt;Overload&gt;d__N</c> type it enumerated
/// first, regardless of which overload's <see cref="System.Reflection.Metadata.MethodDefinitionHandle"/>
/// was actually being resolved — reading <c>[AsyncStateMachine]</c> directly off the dequeued handle
/// has no name to collide on.</description></item>
/// </list>
/// </summary>
public static class ProbeAction
{
    public static async Task ReachesForbiddenViaLambdaAndLocalFunction()
    {
        await Task.Yield();

        Func<Task> lambda = async () =>
        {
            await Task.Yield();
            new ForbiddenRepository().Touch();
        };
        await lambda();

        await LocalAsync();

        async Task LocalAsync()
        {
            await Task.Yield();
            new ForbiddenRepository().Touch();
        }
    }

    public static async Task ProbeEntryForOverload()
    {
        await Overload(1);
    }

    // Declared FIRST — harmless. Shares a name with the overload below on purpose (MED-1).
    static async Task Overload()
    {
        await Task.Yield();
    }

    // Declared SECOND — the one ProbeEntryForOverload actually calls, and the only one that reaches
    // the forbidden repository.
    static async Task Overload(int x)
    {
        await Task.Yield();
        new ForbiddenRepository().Touch();
    }
}
