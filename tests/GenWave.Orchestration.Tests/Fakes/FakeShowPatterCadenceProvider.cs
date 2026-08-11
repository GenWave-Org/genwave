using GenWave.Core.Abstractions;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Mutable <see cref="IShowPatterCadenceProvider"/> double (SPEC F116.3, STORY-308, PLAN T249) —
/// mirrors <see cref="FakeCadenceProvider"/> one seam over. Set <see cref="PatterCadenceMinutes"/>
/// between calls to simulate a live <c>IOptionsMonitor&lt;StationOptions&gt;</c> reload without
/// standing up a real options stack in a unit test.
/// </summary>
sealed class FakeShowPatterCadenceProvider(int patterCadenceMinutes) : IShowPatterCadenceProvider
{
    public int PatterCadenceMinutes { get; set; } = patterCadenceMinutes;
}
