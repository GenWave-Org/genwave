// STORY-485 — The markers are reported (gh-#9 + gh-#868 · SPEC F211.5–F211.6 · PLAN T593, T594)
//
// BDD specification — xUnit. T593 un-skips AC1–AC3 and AC6–AC10 (the /api/status + boot-WARN half);
// ScenarioEngineConfig/ScenarioTheEngineEntrypoint (AC4/AC5, the engine-config half) stay
// [Fact(Skip = PendingEngine)] — T594's job.
//
// Every in-scope scenario boots the real Program.cs graph through WebApplicationFactory<Program> with
// a FakeSchemaJournal (never a real Postgres connection — SchemaVersionDriftHostedService is the only
// hosted service kept; every other one removed, mirrors Story192_PersonaCardMigrationBootSafety.cs's
// own "keep just the one seam under test" idiom) and IAppVersion stamped "5.14.0+abc1234" via
// AppVersion.From. SchemaDriftArc (IAsyncLifetime) arranges once per scenario class and captures the
// read-only results every fact in that class asserts against — the AboutArc/Story474_About.cs "arrange
// once, many read-only facts" idiom. The boot WARN is captured on a logger scoped to ONLY
// ILogger<SchemaVersionDriftHostedService> (mirrors Story192's own per-component CapturingLogger<T>) —
// no ambient boot narration from any other component can land in it, so ScenarioStatusOnAMatchedStation
// .NoWarn's Assert.Empty is exact, not a filtered approximation. AC9's "the station plays" reads
// GET /health returning 200 — the Story068_LegalizeEmptySafeScope.cs "host reaches ready" precedent;
// no live Liquidsoap/Icecast stack exists at this test layer.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GenWave.Core;
using GenWave.Core.Abstractions;
using GenWave.Host.Api;

namespace GenWave.Host.Tests.Specs;

public static class FeatureVersionMarkers
{
    const string PendingEngine = "pending: T594 — engine-config GW_APP_VERSION (STORY-485)";

    public sealed class ScenarioStatusOnAMatchedStation(MatchedStationArc arc) : IClassFixture<MatchedStationArc>
    {
        // Given: IAppVersion "5.14.0+abc1234"; fake journal Applied == SchemaVersion.Expected; GET /api/status

        /// <summary>AC1 — version.app is "v5.14.0"</summary>
        [Fact]
        public void App()
        {
            using var doc = JsonDocument.Parse(arc.StatusBodyJson);
            Assert.Equal(
                "v5.14.0",
                doc.RootElement.GetProperty("version").GetProperty("app").GetString());
        }

        /// <summary>AC2 — version.build is "5.14.0+abc1234"</summary>
        [Fact]
        public void Build()
        {
            using var doc = JsonDocument.Parse(arc.StatusBodyJson);
            Assert.Equal(
                "5.14.0+abc1234",
                doc.RootElement.GetProperty("version").GetProperty("build").GetString());
        }

        /// <summary>AC3 — version.schema.expected equals SchemaVersion.Expected</summary>
        [Fact]
        public void SchemaExpected()
        {
            using var doc = JsonDocument.Parse(arc.StatusBodyJson);
            Assert.Equal(
                SchemaVersion.Expected,
                doc.RootElement.GetProperty("version").GetProperty("schema").GetProperty("expected").GetInt32());
        }

        /// <summary>AC3 — version.schema.applied equals SchemaVersion.Expected</summary>
        [Fact]
        public void SchemaApplied()
        {
            using var doc = JsonDocument.Parse(arc.StatusBodyJson);
            Assert.Equal(
                SchemaVersion.Expected,
                doc.RootElement.GetProperty("version").GetProperty("schema").GetProperty("applied").GetInt32());
        }

        /// <summary>AC6 — no schema WARN at boot. Exact (not filtered): arc's logger is scoped to
        /// ONLY ILogger&lt;SchemaVersionDriftHostedService&gt;, so nothing else can land in it.</summary>
        [Fact]
        public void NoWarn() => Assert.Empty(arc.Warnings);
    }

    public sealed class ScenarioEngineConfig
    {
        // Given: IAppVersion "5.14.0+abc1234"; GET /internal/engine-config

        /// <summary>AC4 — exactly four keys</summary>
        [Fact(Skip = PendingEngine)]
        public void FourKeys() => Assert.Fail(PendingEngine);

        /// <summary>AC4 — the last line is GW_APP_VERSION=v5.14.0</summary>
        [Fact(Skip = PendingEngine)]
        public void CarriesTheVersion() => Assert.Fail(PendingEngine);
    }

    public sealed class ScenarioTheEngineEntrypoint
    {
        // Given: engine/entrypoint.sh run against a stub engine-config serving GW_APP_VERSION=v5.14.0,
        //        liquidsoap stubbed on PATH

        /// <summary>AC5 — one boot line names v5.14.0</summary>
        [Fact(Skip = PendingEngine)]
        public void LogsTheVersion() => Assert.Fail(PendingEngine);

        /// <summary>AC5 — the script references GW_APP_VERSION only in that log line</summary>
        [Fact(Skip = PendingEngine)]
        public void UsesItForNothingElse() => Assert.Fail(PendingEngine);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioSchemaBehind(SchemaBehindArc arc) : IClassFixture<SchemaBehindArc>
    {
        // Given: fake journal Applied == SchemaVersion.Expected - 2; api boot; GET /api/status

        /// <summary>AC7 — exactly one schema WARN</summary>
        [Fact]
        public void OneWarn() => Assert.Single(arc.Warnings);

        /// <summary>AC7 — the WARN names the applied and expected numbers and ./migrate.sh</summary>
        [Fact]
        public void WarnNamesBothAndTheFix() =>
            Assert.Equal(
                $"Schema drift: database has applied {SchemaVersion.Expected - 2} but this build expects {SchemaVersion.Expected} — run ./migrate.sh",
                Assert.Single(arc.Warnings));

        /// <summary>AC9 — status still returns 200 (drift never blocks), and "the station plays":
        /// GET /health also still returns 200 (see this file's own header remarks for why /health is
        /// the proxy used here) — one tuple-equality assert for both (Gh113_PurgeUnavailableEndpoint.cs's
        /// own compound-assert idiom), not two separate assertions in one fact.</summary>
        [Fact]
        public void StatusServes() =>
            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (arc.StatusCode, arc.HealthCode));
    }

    public sealed class ScenarioEmptyJournal(EmptyJournalArc arc) : IClassFixture<EmptyJournalArc>
    {
        // Given: fake journal Applied null; api boot; GET /api/status

        /// <summary>AC8 — exactly one WARN naming SchemaVersion.Expected, "none" and ./migrate.sh</summary>
        [Fact]
        public void WarnNamesNone() =>
            Assert.Equal(
                $"Schema drift: database has applied none but this build expects {SchemaVersion.Expected} — run ./migrate.sh",
                Assert.Single(arc.Warnings));

        /// <summary>AC8 — version.schema.applied is null</summary>
        [Fact]
        public void AppliedIsNull()
        {
            using var doc = JsonDocument.Parse(arc.StatusBodyJson);
            Assert.Equal(
                JsonValueKind.Null,
                doc.RootElement.GetProperty("version").GetProperty("schema").GetProperty("applied").ValueKind);
        }
    }

    public sealed class ScenarioJournalThrows(JournalThrowsArc arc) : IClassFixture<JournalThrowsArc>
    {
        // Given: fake journal GetAppliedAsync throws; api boot; GET /api/status

        /// <summary>AC10 — status returns 200</summary>
        [Fact]
        public void StatusServes() => Assert.Equal(HttpStatusCode.OK, arc.StatusCode);

        /// <summary>AC10 — the WARN fires exactly once</summary>
        [Fact]
        public void OneWarn() => Assert.Single(arc.Warnings);

        /// <summary>AC10 — version.schema.applied is null</summary>
        [Fact]
        public void AppliedIsNull()
        {
            using var doc = JsonDocument.Parse(arc.StatusBodyJson);
            Assert.Equal(
                JsonValueKind.Null,
                doc.RootElement.GetProperty("version").GetProperty("schema").GetProperty("applied").ValueKind);
        }
    }
}

// ── In-process fakes ─────────────────────────────────────────────────────────────────────────────

/// <summary>ISchemaJournal test double — a fixed Applied value, or (when <paramref name="throwing"/>
/// is given) the "any OTHER failure" case SchemaVersionDriftHostedService's own remarks describe
/// (SchemaJournalRepository already degrades 42P01/empty to null itself; this fake stands in for a
/// genuine infra fault one level up, e.g. the database unreachable at boot). Never opens a real
/// connection.</summary>
file sealed class FakeSchemaJournal(int? applied, Exception? throwing = null) : ISchemaJournal
{
    public Task<int?> GetAppliedAsync(CancellationToken ct) =>
        throwing is not null ? Task.FromException<int?>(throwing) : Task.FromResult(applied);
}

/// <summary>Minimal <see cref="ILogger{T}"/> that collects Warning-and-above messages — mirrors
/// Story192_PersonaCardMigrationBootSafety.cs's own file-scoped copy of this idiom (each spec file
/// defines its own, deliberately scoped to ONE component's <see cref="ILogger{T}"/> registration
/// rather than a whole-host <c>ILoggerProvider</c>, so a scenario's "no WARN" fact needs no filtering
/// to stay exact). Backed by <see cref="ConcurrentQueue{T}"/> — <see cref="SchemaVersionDriftHostedService"/>
/// logs from a background-service thread while the arrange reads it back after awaiting
/// <see cref="SchemaVersionStatus.Checked"/>, possibly on a different thread pool thread.</summary>
file sealed class CapturingLogger<T> : ILogger<T>
{
    readonly ConcurrentQueue<string> warnings = new();

    public IReadOnlyCollection<string> Warnings => warnings;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Warning)
            warnings.Enqueue(formatter(state, exception));
    }
}

// ── WebApplicationFactory for every STORY-485 status/drift scenario ─────────────────────────────────

/// <summary>
/// Boots the real Program.cs graph with <paramref name="journal"/> standing in for the real,
/// Postgres-backed <see cref="ISchemaJournal"/>, IAppVersion stamped "5.14.0+abc1234" (this file's own
/// header remarks), and every hosted service removed EXCEPT <see cref="SchemaVersionDriftHostedService"/>
/// — mirrors Story084_StatusEndpoint.cs's <c>StatusApiWebFactory</c> for the catalog/rotation/persona
/// fakes <see cref="GenWave.Host.Api.StatusController"/> also needs, plus
/// Story192_PersonaCardMigrationBootSafety.cs's "keep just the one hosted service under test" idiom.
/// </summary>
file sealed class SchemaDriftWebFactory(ISchemaJournal journal) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-t593-schema-drift";

    internal CapturingLogger<SchemaVersionDriftHostedService> Logger { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureTestServices(services =>
        {
            // Keep ONLY the hosted service under test — no Liquidsoap/theme/persona/dependency-health
            // connections belong in this fact.
            services.RemoveAll<IHostedService>();
            services.AddHostedService<SchemaVersionDriftHostedService>();

            services.RemoveAll<ISchemaJournal>();
            services.AddSingleton(journal);

            // AC1/AC2 read THIS stamp back off GET /api/status directly (this file's own header
            // remarks) — a fixed, known value, not the test assembly's own real build stamp (that
            // reader is Story474_About.cs's job, SPEC F211.2/PLAN T589).
            services.RemoveAll<IAppVersion>();
            services.AddSingleton<IAppVersion>(AppVersion.From("5.14.0+abc1234"));

            // Every other StatusController dependency that would otherwise need a live Postgres —
            // mirrors StatusApiWebFactory exactly; no scenario here asserts on any of these.
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IMediaRotationSink>();
            services.AddSingleton<IMediaRotationSink>(new FakeMediaRotationSink());
            services.RemoveAll<IRotFindingStore>();
            services.AddSingleton<IRotFindingStore>(new FakeRotFindingStore());
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());

            // Registered after logging's own open-generic ILogger<> binding, so this specific
            // closed-generic singleton wins on resolution for SchemaVersionDriftHostedService.
            services.AddSingleton<ILogger<SchemaVersionDriftHostedService>>(Logger);
        });
    }

    internal static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

// ── Shared arc: arrange once per scenario, many read-only facts ─────────────────────────────────────

/// <summary>
/// Arranges ONE <see cref="SchemaDriftWebFactory"/> boot per scenario class (the AboutArc/
/// Story474_About.cs "arrange once, many read-only facts" idiom) and captures the read-only results
/// every fact in that scenario asserts against. Awaits <see cref="SchemaVersionStatus.Checked"/>
/// before reading <c>/api/status</c> — <see cref="SchemaVersionDriftHostedService"/> is
/// fire-and-forget (BackgroundService.StartAsync returns before ExecuteAsync's own first await
/// completes), so the very first request could otherwise race the boot check. <c>Checked</c> is a
/// real completion signal (a <see cref="TaskCompletionSource"/>-backed <see cref="Task"/>), not a
/// polled flag — <see cref="Task.WaitAsync(TimeSpan)"/> throws <see cref="TimeoutException"/> if the
/// boot check never completes, rather than continuing silently past a deadline.
/// </summary>
public abstract class SchemaDriftArc(int? applied = null, Exception? throwing = null) : IAsyncLifetime
{
    /// <summary>The raw <c>/api/status</c> response body — every fact reads it back through
    /// <see cref="JsonDocument"/> itself rather than a deserialized DTO (mirrors AboutArc's own
    /// "compare the untouched JSON" rationale one file over).</summary>
    public string StatusBodyJson { get; private set; } = "";

    public HttpStatusCode StatusCode { get; private set; }

    /// <summary>AC9's "the station plays" proxy — see this file's own header remarks.</summary>
    public HttpStatusCode HealthCode { get; private set; }

    public IReadOnlyList<string> Warnings { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await using var factory = new SchemaDriftWebFactory(new FakeSchemaJournal(applied, throwing));
        var client = await SchemaDriftWebFactory.LoggedInClientAsync(factory);

        var status = factory.Services.GetRequiredService<SchemaVersionStatus>();
        await status.Checked.WaitAsync(TimeSpan.FromSeconds(10));

        var statusResponse = await client.GetAsync("/api/status");
        StatusCode = statusResponse.StatusCode;
        StatusBodyJson = await statusResponse.Content.ReadAsStringAsync();

        var healthResponse = await client.GetAsync("/health");
        HealthCode = healthResponse.StatusCode;

        // Snapshot, not the live queue — every fact reads a frozen list, never one that could still
        // be mutating on the background service's own thread.
        Warnings = factory.Logger.Warnings.ToArray();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class MatchedStationArc() : SchemaDriftArc(applied: SchemaVersion.Expected)
{
}

public sealed class SchemaBehindArc() : SchemaDriftArc(applied: SchemaVersion.Expected - 2)
{
}

public sealed class EmptyJournalArc() : SchemaDriftArc(applied: null)
{
}

public sealed class JournalThrowsArc() : SchemaDriftArc(throwing: new InvalidOperationException("database unreachable"))
{
}
