// gh-#224 — The schedule grid and the taste gates follow the station: provider half.
//
// BDD specification — xUnit. gh-#224 extends IStationClockProvider with Zone — the full
// TimeZoneInfo ScheduleResolver's DST-aware boundary math needs (no single LocalNow offset can
// reconstruct a zone's transition rules). What THIS file owns: OptionsMonitorStationClockProvider
// resolves Zone from the SAME live Station:Timezone read LocalNow already goes through — a
// configured IANA id yields that zone, empty (the fresh-deploy default) and an unresolvable id
// both fall back to the container's own zone, the pre-gh-#224 behavior unchanged. The consumer
// pins (taste gating, slot resolution) live in
// Orchestration.Tests/Specs/Gh224_StationZoneScheduleAndTasteClock.cs (the Story117/121 split:
// facts live where their subject compiles); the PUT-repoints-live half is already pinned for the
// shared ResolveTimeZone read by Gh117_StationTimezoneSetting.cs.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

public static class FeatureStationZoneProvider
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>A real options monitor over an in-memory <c>Station</c> section — the minimal
    /// slice of Gh117_StationTimezoneSetting's live rig these read-only Zone facts need.</summary>
    static OptionsMonitorStationClockProvider BuildClock(string timezone, FakeTimeProvider time)
    {
        var root = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Station:Id"] = "s1",
                ["Station:Name"] = "GenWave",
                ["Station:Voice"] = "af_heart",
                ["Station:Timezone"] = timezone,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<StationOptions>().Bind(root.GetSection(StationOptions.Section));
        var monitor = services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<StationOptions>>();
        return new OptionsMonitorStationClockProvider(monitor, time);
    }

    /// <summary>The stand-in container zone — fixed-offset, no DST, and nothing any spec here
    /// configures as the station's own, so a fallback is unmistakable.</summary>
    static TimeZoneInfo ContainerZone => TimeZoneInfo.FindSystemTimeZoneById("Australia/Brisbane");

    static FakeTimeProvider BuildContainerTime()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 20, 2, 0, 0, TimeSpan.Zero));
        time.SetLocalTimeZone(ContainerZone);
        return time;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — a configured id resolves to the station's own zone
    // ---------------------------------------------------------------------

    public sealed class ScenarioAConfiguredTimezoneYieldsItsZone
    {
        [Fact]
        public void ZoneIsTheConfiguredIanaZone()
        {
            var clock = BuildClock("America/Edmonton", BuildContainerTime());

            Assert.Equal("America/Edmonton", clock.Zone.Id);
        }
    }

    // ── Sad paths — empty and garbage both keep the container's own zone ────────────────────────

    public sealed class ScenarioAnEmptyTimezoneKeepsTheContainersZone
    {
        [Fact]
        public void ZoneIsTheContainersOwn()
        {
            // The fresh-deploy shape (empty = honest blank): Zone is the container's zone — so a
            // ScheduleResolver wired through the seam behaves byte-identically to one that never
            // was, the prior-behavior pin.
            var clock = BuildClock(string.Empty, BuildContainerTime());

            Assert.Equal(ContainerZone.Id, clock.Zone.Id);
        }
    }

    public sealed class ScenarioAnUnresolvableTimezoneNeverFaultsTheGrid
    {
        [Fact]
        public void GarbageFallsBackToTheContainersZone()
        {
            // Garbage can only arrive via the environment (SettingValidator 400s it on the
            // settings-API path) — the grid keeps resolving on the container's clock rather than
            // throwing into every feeder tick (mirrors LocalNow's own gh-#117 guarantee).
            var clock = BuildClock("Not/AZone", BuildContainerTime());

            Assert.Equal(ContainerZone.Id, clock.Zone.Id);
        }
    }
}
