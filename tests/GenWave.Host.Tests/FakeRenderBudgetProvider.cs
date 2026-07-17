using GenWave.Core.Abstractions;

namespace GenWave.Host.Tests;

/// <summary>
/// Mutable <see cref="IRenderBudgetProvider"/> double (SPEC F44.2, mirrors <see cref="FakeCadenceProvider"/>
/// one seam over). Set <see cref="Budget"/> between calls to simulate a live
/// <c>IOptionsMonitor&lt;TtsOptions&gt;</c> reload without standing up a real options stack in a
/// unit test.
/// </summary>
sealed class FakeRenderBudgetProvider(TimeSpan budget) : IRenderBudgetProvider
{
    public TimeSpan Budget { get; set; } = budget;

    public TimeSpan Current => Budget;
}
