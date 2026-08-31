// STORY-329 — The booth log's read side knows a crosstalk row when it sees one (SPEC F127.11,
// PLAN T287, round-2 review finding F3)
//
// BDD specification — xUnit. Pre-fix, BoothLogController.ToPickDto tried ONLY
// BoothLogPickStampSerializer against every row's stored pick — a Crosstalk row's own
// {"lines":[...]} shape has neither firedRules nor isExploration, so it deserialized to a
// null-FiredRules BoothLogPickStamp and logged a FALSE "off-schema pick stamp" WARN for every single
// crosstalk row, discarding the script entirely. The fix (this file's own scope): try
// CrosstalkAiredScriptSerializer FIRST — now validated (F9) to answer null for anything that is not
// genuinely a crosstalk script — so a crosstalk row's pick lands on its own BoothLogEntryDto.Crosstalk
// field, with NO warning logged, and an ordinary persona-pick row falls through to the pre-existing
// path exactly as before (already proven by Story217_BoothLogPickStamp.cs, untouched by this file).
//
// Mirrors Story217_BoothLogPickStamp.cs's own ScenarioApiExposesTheStamp harness idiom (Story123's
// controller/factory idiom) — a fixed-row FakeBoothLogReader, the real BoothLogController, the response
// serialized exactly as ASP.NET Core's default camelCase JsonOptions would. Each harness type here is
// its own copy (file-scoped types cannot cross files) rather than a shared extraction — the two files'
// own concerns are different enough (persona pick vs. crosstalk script) that a shared harness would
// mostly be indirection.

using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

/// <summary>In-memory <see cref="IBoothLogReader"/> double for this file's facts — a fixed row set, no
/// keyset paging (mirrors Story217_BoothLogPickStamp.cs's own <c>ApiFakeBoothLogReader</c>, copied
/// rather than shared since that type is itself <see langword="file"/>-private to its own file).
/// </summary>
file sealed class ApiFakeBoothLogReader(IReadOnlyList<BoothLogEntry> rows) : IBoothLogReader
{
    public Task<BoothLogPage> ReadAsync(BoothLogCursor? before, int take, CancellationToken ct) =>
        Task.FromResult(new BoothLogPage(rows.Take(take).ToList(), NextBefore: null));

    public Task<long?> GetMediaIdAsync(long id, CancellationToken ct) =>
        Task.FromResult(rows.FirstOrDefault(e => e.Id == id)?.MediaId);

    /// <summary>T362 review HIGH-2: IBoothLogReader.GetLastAiringAsync is now abstract — this file's own facts never touch a show's last airing, so this double answers "none" unconditionally.</summary>
    public Task<ShowLastAiring?> GetLastAiringAsync(long showId, CancellationToken ct) =>
        Task.FromResult<ShowLastAiring?>(null);

    /// <summary>T367: IBoothLogReader.GetTrackAiringAsync is now abstract — this file's own facts never touch the station-thumb action, so this double answers "row not found" unconditionally.</summary>
    public Task<BoothLogAiring?> GetTrackAiringAsync(long id, CancellationToken ct) =>
        Task.FromResult<BoothLogAiring?>(null);
}

/// <summary>Minimal <see cref="ILogger{T}"/> that collects every logged message, tagged with its
/// <see cref="LogLevel"/> — mirrors <c>GenWave.Orchestration.Tests.Fakes.CapturingLogger&lt;T&gt;</c>'s
/// own shape (file-scoped here since Host.Tests keeps no shared copy).</summary>
file sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}

file sealed class NotSupportedPersonaTasteAccrualStore : IPersonaTasteAccrualStore
{
    public Task<TasteThumbOutcome> ThumbAsync(long boothLogId, TasteThumbDirection direction, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by this file's facts.");
}

/// <summary>T367: none of this file's facts exercise <see cref="BoothLogController.ThumbStation"/> —
/// only the read/pick-projection facts construct <see cref="BoothLogController"/> here.</summary>
file sealed class NotSupportedThumbStore : IThumbStore
{
    public Task<ThumbWriteResult> RecordAsync(
        long mediaId, DateTimeOffset airingStartedAt, string listenerKey,
        ThumbDirection direction, ThumbSource source, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by this file's facts.");

    public Task<int> CountByListenerSinceAsync(string listenerKey, DateTimeOffset since, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by this file's facts.");

    public Task<int> SweepAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by this file's facts.");

    public Task RecomputeAllAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by this file's facts.");
}

file static class BoothLogApiControllerFactory
{
    public static (BoothLogController Controller, CapturingLogger<BoothLogController> Logger) Build(IBoothLogReader reader)
    {
        var logger = new CapturingLogger<BoothLogController>();
        var controller = new BoothLogController(
            reader, new NotSupportedPersonaTasteAccrualStore(), new FakeMediaLibraryMembership(),
            new FakeSafeScopeProvider(), new NotSupportedThumbStore(), logger);
        return (controller, logger);
    }
}

file static class ApiWireJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

public static class FeatureCrosstalkBoothRead
{
    static CrosstalkAiredScript SampleScript() => new(
    [
        new CrosstalkAiredLine(CrosstalkSpeaker.Host, "Did you catch that new single?", false),
        new CrosstalkAiredLine(CrosstalkSpeaker.Neighbor, "I did — it's on repeat over here.", true),
    ]);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioACrosstalkRowSurfacesItsScript
    {
        static async Task<(JsonElement Entry, IReadOnlyList<(LogLevel Level, string Message)> LogEntries)> DriveOneCrosstalkRowAsync()
        {
            var entry = new BoothLogEntry(
                1, DateTime.UtcNow, "track-started", "Started 'GenWave' by Nova", PersonaId: null,
                Pick: CrosstalkAiredScriptSerializer.Serialize(SampleScript()));

            var (controller, logger) = BoothLogApiControllerFactory.Build(new ApiFakeBoothLogReader([entry]));
            var result = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(
                await controller.List(before: null, take: 10, CancellationToken.None));

            var json = JsonSerializer.Serialize(result.Value, ApiWireJson.Options);
            using var document = JsonDocument.Parse(json);
            return (document.RootElement.GetProperty("entries")[0].Clone(), logger.Entries);
        }

        [Fact]
        public static async Task TheEntryCarriesTheFullScriptOnItsOwnCrosstalkField()
        {
            var (entry, _) = await DriveOneCrosstalkRowAsync();

            var lines = entry.GetProperty("crosstalk").GetProperty("lines").EnumerateArray()
                .Select(line => (Speaker: line.GetProperty("speaker").GetString(), Text: line.GetProperty("text").GetString()))
                .ToList();
            Assert.Equal([("Host", "Did you catch that new single?"), ("Neighbor", "I did — it's on repeat over here.")], lines);
        }

        [Fact]
        public static async Task TheEntrysPickFieldIsAbsent()
        {
            // A crosstalk row's script and a persona pick are mutually exclusive on the wire — never both.
            var (entry, _) = await DriveOneCrosstalkRowAsync();

            Assert.False(entry.TryGetProperty("pick", out _));
        }

        [Fact]
        public static async Task NoWarningIsLoggedForAGenuineCrosstalkRow()
        {
            // The exact pre-fix defect: every crosstalk row logged a false "off-schema pick stamp"
            // WARN. A genuine crosstalk script is this row's OWN valid shape, not corruption.
            var (_, logEntries) = await DriveOneCrosstalkRowAsync();

            Assert.DoesNotContain(logEntries, e => e.Level == LogLevel.Warning);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the control: an ordinary persona-pick row is unaffected
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnOrdinaryPersonaPickRowIsUnaffected
    {
        [Fact]
        public static async Task APersonaPickRowStillCarriesPickNotCrosstalk()
        {
            // Given a track-start row stamped with an ORDINARY persona pick, never a crosstalk script
            var stamp = new BoothLogPickStamp([new BoothLogFiredRuleSummary("The Weeknd", 0.6)], IsExploration: false);
            var entry = new BoothLogEntry(
                2, DateTime.UtcNow, "track-started", "Started 'Night Drive' by The Waveforms",
                PersonaId: 7, Pick: BoothLogPickStampSerializer.Serialize(stamp));

            var (controller, _) = BoothLogApiControllerFactory.Build(new ApiFakeBoothLogReader([entry]));
            var result = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(
                await controller.List(before: null, take: 10, CancellationToken.None));
            var json = JsonSerializer.Serialize(result.Value, ApiWireJson.Options);
            using var document = JsonDocument.Parse(json);
            var wireEntry = document.RootElement.GetProperty("entries")[0];

            // Then the crosstalk-first dispatch never misclassifies it: pick is present, crosstalk is
            // absent — the CrosstalkAiredScriptSerializer attempt tried first correctly answers null
            // for this shape (SPEC F9) and falls through to the pre-existing persona-pick path.
            Assert.True(wireEntry.TryGetProperty("pick", out _));
            Assert.False(wireEntry.TryGetProperty("crosstalk", out _));
        }
    }
}
