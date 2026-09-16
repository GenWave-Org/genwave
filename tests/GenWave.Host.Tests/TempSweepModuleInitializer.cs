using System.Runtime.CompilerServices;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests;

/// <summary>
/// Sweeps <see cref="Path.GetTempPath"/> once per Host.Tests process (gh-#710, STORY-441, PLAN
/// T479): the dev box accumulated ~60k <c>gw-*</c> scratch directories because specs that call
/// <c>Directory.CreateTempSubdirectory</c> directly have no guaranteed cleanup path when a run is
/// killed mid-test (a debugger stop, a CI cancellation, a crash) — <see cref="TempDir.Dispose"/>
/// never runs, and the OS never reclaims <c>/tmp</c> on its own between container runs.
///
/// Removes every directory under the real temp root whose name starts with <see
/// cref="TempSweep.Prefixes"/> (<c>gw-</c>, <c>genwave-pawire-</c>, <c>story343-env-</c>,
/// <c>gh332-</c>) and is more than an hour old. The one-hour floor is deliberate, not arbitrary:
/// a sibling Host.Tests process running concurrently on the same box (a second CI shard, a
/// developer's parallel `dotnet test` invocation) may have fresh directories of its own under
/// those same prefixes, and this sweep must never delete out from under a suite that is still
/// running — only directories old enough to belong to a run that has already finished are fair
/// game.
///
/// Wrapped in a swallow-everything try/catch: a sweep is best-effort housekeeping, never a test
/// dependency, so a failure here (permissions, a raced deletion, anything) must not fail the
/// suite it is trying to keep tidy.
/// </summary>
static class TempSweepModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        try
        {
            TempSweep.Run(Path.GetTempPath(), TimeSpan.FromHours(1));
        }
        catch (Exception)
        {
            // Best-effort cleanup only — never allowed to fail the suite it is tidying up after.
        }
    }
}
