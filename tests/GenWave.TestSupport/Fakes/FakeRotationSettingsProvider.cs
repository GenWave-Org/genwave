using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.TestSupport.Fakes;

/// <summary>
/// Mutable <see cref="IRotationSettingsProvider"/> double (SPEC F41.6, mirrors <see cref="FakeCadenceProvider"/>
/// one seam over). Set <see cref="Settings"/> between calls to simulate a live
/// <c>IOptionsMonitor&lt;StationOptions&gt;</c> reload without standing up a real options stack in a
/// unit test.
/// </summary>
public sealed class FakeRotationSettingsProvider(RotationSettings settings) : IRotationSettingsProvider
{
    /// <summary>The rotation settings a spec can mutate between calls.</summary>
    public RotationSettings Settings { get; set; } = settings;

    /// <inheritdoc/>
    public RotationSettings Current => Settings;
}
