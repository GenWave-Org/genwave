// STORY-069 — Distinguish empty-scope vs empty-catalog at /internal/safe-track
//
// BDD specification — xUnit. SPEC F25.3: the two 204 branches of
// InternalEndpoints.HandleSafeTrackAsync must emit distinct structured WARN log
// lines — "SafeScope empty (F4.4 degraded mode)" for the config branch,
// "SafeScope has libraries but no ready+measurable+eligible rows" for the data
// branch. The 200 (annotate) path stays silent.
//
// T507: the 204-shape/Cache-Control regression facts this file used to carry (obsolete —
// EmptyScopeReturns204WithEmptyBody, EmptyCatalogReturns204WithEmptyBody,
// BothTwoOhFourBranchesStampCacheControlNoStore) are superseded by Story056_SafeTrackEndpoint.cs's
// own EmptySafeScopeReturns204/NoReadyRowsInScopeReturns204/ResponseCarriesCacheControlNoStore —
// same handler, same fakes, already live. The log-branch facts below were never actually re-pinned
// live anywhere else, so they are un-skipped for real here instead, against
// InternalEndpoints.HandleSafeTrackAsync directly (no container: the handler is a plain in-process
// call, same seam Story056 uses).

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Artwork;
using GenWave.Host.Engine;
using GenWave.Host.Options;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;
using TrackLoudness = GenWave.Core.Domain.Loudness;

namespace GenWave.Host.Tests.Specs;

public static class FeatureSafeTrackDistinctWarnLogs
{
    static ArtworkUrlResolver NoArtworkResolver() =>
        new(new FakeOptionsMonitor<StationOptions>(new StationOptions()), new FakeArtworkTokenStore(),
            new FakeActivePersonaAccessor(), new PersonaAvatarTokenCache(new FakePersonaAvatarStore(), TimeProvider.System, NullLogger<PersonaAvatarTokenCache>.Instance), new StationImageCache(new FakeStationImageStore(), TimeProvider.System, NullLogger<StationImageCache>.Instance));

    static StationOptions BuildStationOptions(IList<long>? safeScope) => new()
    {
        Id = "test-station",
        Name = "Test Station",
        Voice = "en-us",
        Scope = new StationScopeOptions { LibraryIds = [1L] },
        SafeScope = new StationScopeOptions { LibraryIds = safeScope ?? [1L] },
    };

    static MediaReference BuildReadyTrack() => new(
        MediaId: "track-001",
        Locator: "/media/track-001.mp3",
        Title: "Safe Track",
        Loudness: new TrackLoudness(-20.0, -2.0, Measurable: true),
        DurationMs: 180_000,
        SampleRate: 44100,
        Channels: 2,
        BitrateKbps: 320,
        Artist: "Test Artist",
        Album: null,
        Genre: null,
        Year: null);

    static Task<IResult> InvokeAsync(FakeMediaCatalog catalog, IList<long>? safeScope, CapturingLoggerProvider logs) =>
        InternalEndpoints.HandleSafeTrackAsync(
            catalog,
            new FakeOptionsMonitor<StationOptions>(BuildStationOptions(safeScope)),
            new FakeOptionsMonitor<LoudnessOptions>(new LoudnessOptions()),
            NoArtworkResolver(),
            logs.CreateLogger("safe-track"),
            new DefaultHttpContext().Response,
            CancellationToken.None);

    // ---------------------------------------------------------------------
    // HAPPY PATH — distinct WARN branches
    // ---------------------------------------------------------------------

    public sealed class ScenarioTwoOhFourBranchesLogDistinctMessages
    {
        [Fact]
        public async Task AnEmptyScopeCallLogsAWarnNamingF44DegradedMode()
        {
            var logs = new CapturingLoggerProvider();

            await InvokeAsync(new FakeMediaCatalog(BuildReadyTrack()), safeScope: [], logs);

            Assert.Contains(logs.Messages, m => m.Contains("SafeScope empty (F4.4 degraded mode)", StringComparison.Ordinal));
        }

        [Fact]
        public async Task ANonEmptyScopeWithNoReadyRowsLogsAWarnNamingTheDataDepletionCase()
        {
            var logs = new CapturingLoggerProvider();

            await InvokeAsync(new FakeMediaCatalog(ready: null), safeScope: [1L], logs);

            Assert.Contains(logs.Messages, m => m.Contains("SafeScope has libraries but no ready+measurable+eligible rows", StringComparison.Ordinal));
        }

        [Fact]
        public async Task TheTwoWarnMessagesAreTextuallyDistinct()
        {
            var emptyScopeLogs = new CapturingLoggerProvider();
            await InvokeAsync(new FakeMediaCatalog(BuildReadyTrack()), safeScope: [], emptyScopeLogs);

            var noRowsLogs = new CapturingLoggerProvider();
            await InvokeAsync(new FakeMediaCatalog(ready: null), safeScope: [1L], noRowsLogs);

            Assert.NotEqual(Assert.Single(emptyScopeLogs.Messages), Assert.Single(noRowsLogs.Messages));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — 200 path stays silent
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnnotationPathEmitsNoWarn
    {
        [Fact]
        public async Task AReadyRowPathReturnsAnnotateAndLogsNoWarn()
        {
            var logs = new CapturingLoggerProvider();

            var result = await InvokeAsync(new FakeMediaCatalog(BuildReadyTrack()), safeScope: [1L], logs);

            var body = Assert.IsType<ContentHttpResult>(result).ResponseContent;
            Assert.StartsWith("annotate:", body, StringComparison.Ordinal);
            Assert.Empty(logs.Messages);
        }
    }
}
