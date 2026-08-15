using GenWave.Core.Abstractions;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Mutable <see cref="ICrosstalkScopeProvider"/> double (SPEC F127.8, STORY-328, PLAN T285) —
/// mirrors <see cref="FakeShowPatterCadenceProvider"/> one seam over. Set
/// <see cref="EnabledShows"/>/<see cref="EveryNthAiring"/> between calls to simulate a live
/// <c>IOptionsMonitor&lt;GenWave.Tts.CrosstalkOptions&gt;</c> reload without standing up a real
/// options stack in a unit test.
/// </summary>
sealed class FakeCrosstalkScopeProvider(IReadOnlyList<string>? enabledShows = null, int everyNthAiring = 1)
    : ICrosstalkScopeProvider
{
    public IReadOnlyList<string> EnabledShows { get; set; } = enabledShows ?? [];

    public int EveryNthAiring { get; set; } = everyNthAiring;
}
