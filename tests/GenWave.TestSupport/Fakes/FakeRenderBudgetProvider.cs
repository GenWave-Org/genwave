using GenWave.Core.Abstractions;

namespace GenWave.TestSupport.Fakes;

/// <summary>
/// Mutable <see cref="IRenderBudgetProvider"/> double (SPEC F44.2, mirrors <see cref="FakeCadenceProvider"/>
/// one seam over). Set <see cref="Budget"/> between calls to simulate a live
/// <c>IOptionsMonitor&lt;TtsOptions&gt;</c> reload without standing up a real options stack in a
/// unit test.
/// </summary>
public sealed class FakeRenderBudgetProvider(TimeSpan budget) : IRenderBudgetProvider
{
    /// <summary>The render budget a spec can mutate between calls.</summary>
    public TimeSpan Budget { get; set; } = budget;

    /// <inheritdoc/>
    public TimeSpan Current => Budget;
}
