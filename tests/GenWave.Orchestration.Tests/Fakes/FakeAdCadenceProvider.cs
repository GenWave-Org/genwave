using GenWave.Core.Abstractions;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Mutable <see cref="IAdCadenceProvider"/> double (SPEC F158.3, STORY-388, PLAN T397) — mirrors
/// <see cref="FakeCadenceProvider"/> one seam over. Set <see cref="EveryNUnits"/> between calls to
/// simulate a live <c>IOptionsMonitor&lt;StationOptions&gt;</c> reload without standing up a real
/// options stack in a unit test.
/// </summary>
sealed class FakeAdCadenceProvider(int everyNUnits) : IAdCadenceProvider
{
    public int EveryNUnits { get; set; } = everyNUnits;

    public int Current => EveryNUnits;
}
