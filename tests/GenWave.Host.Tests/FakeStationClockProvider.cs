using GenWave.Core.Abstractions;

namespace GenWave.Host.Tests;

/// <summary>
/// Mutable <see cref="IStationClockProvider"/> double (gh-#117, mirrors
/// <see cref="FakeStationIdentityProvider"/> one seam over). Set <see cref="Now"/> between calls to
/// simulate a live <c>Station:Timezone</c> change without standing up a real options stack in a
/// unit test.
/// </summary>
sealed class FakeStationClockProvider(DateTimeOffset now) : IStationClockProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public DateTimeOffset LocalNow => Now;
}
