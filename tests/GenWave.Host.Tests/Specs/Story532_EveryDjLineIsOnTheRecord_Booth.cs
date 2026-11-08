// STORY-532 — Every DJ line is on the record (gh-#293 gh-#920 · SPEC F218 · PLAN T684 T685)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-08); the named task
// activates its facts, watches them fail, then makes them pass.
//
// Spans three files; see tests/GenWave.Tts.Tests/Specs/Story532_EveryDjLineIsOnTheRecord_Copy.cs for the map.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Host.Tests.Fakes;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// Drives one <see cref="SegmentGenerated"/> through the real booth-log write path (public
/// <c>AddBoothLog</c> DI surface, only the appender faked) — the Story217 harness idiom.
/// </summary>
file static class PatterAiredHarness
{
    public static async Task<BoothLogAppendRequest> DriveAsync(SegmentGenerated evt)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        services.AddBoothLog("Host=nowhere;Database=test", new ConfigurationBuilder().Build());

        var appender = new FakeBoothLogAppender();
        services.AddSingleton<IBoothLogAppender>(appender);

        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToList();
        foreach (var service in hosted)
            await service.StartAsync(CancellationToken.None);

        provider.GetRequiredService<IBoothLogEventConsumer>().Publish(evt);
        await appender.WaitForCallsAsync(1, TimeSpan.FromSeconds(5));

        foreach (var service in hosted)
            await service.StopAsync(CancellationToken.None);

        return Assert.Single(appender.Calls);
    }
}

public static class FeatureEveryDjLineIsOnTheRecordBooth
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC3 — the booth feed quotes it
    // entry point: BoothLogWriter via the event bus (SegmentGenerated)
    public sealed class ScenarioTheBoothFeedQuotesIt
    {
        // Given a SegmentGenerated event carrying PersonaId, ShowId and UtteranceId
        // When  BoothLogWriter publishes it

        static readonly string Copy = new string('a', 60) + new string('b', 60);

        static Task<BoothLogAppendRequest> DriveAsync() =>
            PatterAiredHarness.DriveAsync(new SegmentGenerated("tts:abc", "LeadIn", "af_heart")
            {
                PersonaId = 7,
                ShowId = 3,
                UtteranceId = 99,
                Text = Copy,
            });

        [Fact]
        public async Task ThePatterAiredRowStampsPersonaId() => Assert.Equal(7, (await DriveAsync()).PersonaId);

        [Fact]
        public async Task ThePatterAiredRowStampsShowId() => Assert.Equal(3, (await DriveAsync()).ShowId);

        [Fact]
        public async Task ItsSummaryEndsWithTheCopySFirst80CharactersInQuotes() =>
            Assert.EndsWith($"\"{Copy[..80]}\"", (await DriveAsync()).Summary, StringComparison.Ordinal);
    }

    // AC3 edge — the 80-character cut never splits a surrogate pair
    public sealed class ScenarioTheCutFallsInsideASurrogatePair
    {
        // Given copy whose 80th character is the high half of a surrogate pair
        // When  BoothLogWriter publishes it

        static readonly string Copy = new string('a', 79) + "\U0001F3B5" + "tail";

        [Fact]
        public async Task ItsQuoteStopsBeforeTheSplitPair()
        {
            var row = await PatterAiredHarness.DriveAsync(new SegmentGenerated("tts:abc", "LeadIn", "af_heart") { Text = Copy });

            Assert.EndsWith($"\"{new string('a', 79)}\"", row.Summary, StringComparison.Ordinal);
        }
    }

    // AC4 — air time is marked
    // entry point: engine metadata callback (gw_tts="true" file started)
    public sealed class ScenarioAirTimeIsMarked
    {
        // Given a stored utterance whose file the engine then starts
        // When  the engine reports a gw_tts="true" file started

        [Fact(Skip = "pending: T685 — see docs/PLAN.md")]
        public void ThatUtteranceSAiredAtIsSet() => Assert.Fail("pending: T685 — that utterance's aired_at is set");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC6 — dropped renders never air
    public sealed class ScenarioDroppedRendersNeverAir
    {
        // Given a stored utterance whose render is later dropped
        // When  no engine event arrives

        [Fact(Skip = "pending: T685 — see docs/PLAN.md")]
        public void AiredAtStaysNull() => Assert.Fail("pending: T685 — aired_at stays null");
    }
}
