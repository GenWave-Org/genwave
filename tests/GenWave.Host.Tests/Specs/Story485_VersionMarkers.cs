// STORY-485 — The markers are reported (gh-#9 + gh-#868 · SPEC F211.5–F211.6 · PLAN T593, T594)
//
// BDD specification — xUnit. T593 un-skipped AC1–AC3 and AC6–AC10 (the /api/status + boot-WARN
// half). T594 (this revision) un-skips ScenarioEngineConfig/ScenarioTheEngineEntrypoint (AC4/AC5,
// the engine-config half): GW_APP_VERSION=<IAppVersion.Display> is now the fourth (and last) key
// GET /internal/engine-config emits (InternalEndpoints' own remarks), and engine/entrypoint.sh
// logs it once at boot and reads it for nothing else (that script's own remarks).
//
// ScenarioEngineConfig reuses SchemaDriftWebFactory (this file's own factory, below) — its
// IAppVersion is already stamped "5.14.0+abc1234" for the /api/status scenarios above, so AC4
// needs no factory of its own. ScenarioTheEngineEntrypoint instead drives the REAL
// engine/entrypoint.sh through ScriptProcess (gh-#776) with curl and liquidsoap stubbed on a
// scratch PATH (ScriptProcess.MakeBinDir/AddStub) — engine-config is faked at the transport edge
// (curl), never by pointing the script at a real host, so no live api or Liquidsoap binary is
// needed. "Reads it for nothing else" is proven at runtime, not by scanning the script's source:
// the liquidsoap stub records its own argv+env before exiting, and the facts assert the recorded
// version-value/key never appear there, plus that genwave.liq's own source never names the key.
// AC5's sad path (a forged-CR value, an ANSI-escape value — ScenarioForgedCarriageReturn/
// ScenarioAnsiEscapeSequence below) drives the same real script with curl stubbed to serve each
// hostile value, asserting the boot line reads the neutralized "GW_APP_VERSION=invalid".
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
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureVersionMarkers
{
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

    public sealed class ScenarioEngineConfig(EngineConfigArc arc) : IClassFixture<EngineConfigArc>
    {
        // Given: IAppVersion "5.14.0+abc1234"; GET /internal/engine-config

        /// <summary>AC4 — exactly four keys</summary>
        [Fact]
        public void FourKeys() => Assert.Equal(4, arc.Lines.Count);

        /// <summary>AC4 — the last line is GW_APP_VERSION=v5.14.0</summary>
        [Fact]
        public void CarriesTheVersion() => Assert.Equal("GW_APP_VERSION=v5.14.0", arc.Lines[^1]);
    }

    public sealed class ScenarioTheEngineEntrypoint(EntrypointArc arc) : IClassFixture<EntrypointArc>
    {
        // Given: engine/entrypoint.sh run against a stub engine-config serving GW_APP_VERSION=v5.14.0,
        //        liquidsoap stubbed on PATH to record its own argv+env before exiting

        /// <summary>AC5 — one boot line names v5.14.0. This is the "one boot line" half of AC5,
        /// proven at runtime (a stderr line count), not by scanning the script's source.</summary>
        [Fact]
        public void LogsTheVersion() =>
            Assert.Single(arc.StdErrLines, l => l.Contains("v5.14.0", StringComparison.Ordinal));

        /// <summary>Proves the recorded argv+env dump this scenario's "never sees" facts assert
        /// against is not vacuously empty: entrypoint.sh DOES export the three crossfade/safe-gap
        /// keys (the curl stub's first three lines, this file's own <see cref="EntrypointRunArc"/>
        /// remarks) into the environment Liquidsoap actually execs into, so a recorder that captured
        /// nothing would fail this fact rather than let the absent-version facts pass by accident.</summary>
        [Fact]
        public void LiquidsoapWasHandedTheTuningKeys() =>
            Assert.Contains("GW_XFADE_MIN=2", arc.LiquidsoapArgsAndEnv, StringComparison.Ordinal);

        /// <summary>AC5 — Liquidsoap itself never receives the version value: neither its argv nor
        /// its environment at exec time (recorded by the liquidsoap stub, this file's own header
        /// remarks) carries it, because <c>app_version</c> is a plain shell variable, never
        /// exported and never passed as an argument.</summary>
        [Fact]
        public void LiquidsoapNeverSeesTheVersionValue() =>
            Assert.DoesNotContain("v5.14.0", arc.LiquidsoapArgsAndEnv, StringComparison.Ordinal);

        /// <summary>AC5 — nor does Liquidsoap ever see the KEY, distinct from the value above: even
        /// under a name Liquidsoap can't itself interpret, GW_APP_VERSION never reaches its argv or
        /// environment either.</summary>
        [Fact]
        public void LiquidsoapNeverSeesTheKey() =>
            Assert.DoesNotContain("GW_APP_VERSION", arc.LiquidsoapArgsAndEnv, StringComparison.Ordinal);

        /// <summary>AC5 — genwave.liq (the script Liquidsoap actually runs) never references the key
        /// either, closing the last place "read by nothing else" could quietly stop being true.</summary>
        [Fact]
        public void TheLiquidsoapScriptNeverReferencesTheKey() =>
            Assert.DoesNotContain("GW_APP_VERSION", arc.GenwaveLiqText, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------
    // AC5 SAD PATH — hostile GW_APP_VERSION values from engine-config
    // ---------------------------------------------------------------------
    //
    // Segregated from ScenarioTheEngineEntrypoint above (a distinct Scenario per hostile value,
    // not a Theory over both — house rule): each drives the REAL entrypoint.sh with curl stubbed
    // to serve a forged GW_APP_VERSION, and asserts the boot line reads the neutralized
    // "GW_APP_VERSION=invalid", never the forged payload. ScriptProcess runs the script under
    // bash; the image's `sh` is dash, whose bracket matching was checked by hand to agree
    // (byte-for-byte, so a raw CR/ESC is simply "not in the class").

    public sealed class ScenarioForgedCarriageReturn(ForgedCrEntrypointArc arc) : IClassFixture<ForgedCrEntrypointArc>
    {
        // Given: engine-config serves GW_APP_VERSION=v1.0<CR>[engine-entrypoint] forged — an
        // attempt to smuggle a second, fake log line into the boot output without ever emitting a
        // real newline (entrypoint.sh's parse loop reads real lines only, via `read -r line`, so
        // this whole payload is ONE hostile value, not two lines)

        /// <summary>AC5 sad path — the forged value is neutralized before it ever reaches the log:
        /// the boot line reads exactly "...GW_APP_VERSION=invalid", carrying neither the raw CR
        /// nor the forged text.</summary>
        [Fact]
        public void LogsInvalid() =>
            Assert.Contains(
                "[engine-entrypoint] control-plane version: GW_APP_VERSION=invalid",
                arc.StdErrLines);
    }

    public sealed class ScenarioAnsiEscapeSequence(AnsiEscapeEntrypointArc arc) : IClassFixture<AnsiEscapeEntrypointArc>
    {
        // Given: engine-config serves GW_APP_VERSION=v1.0<ESC>[31mred — the other classic
        // terminal-log-injection payload shape alongside a raw CR (a colour-code escape)

        /// <summary>AC5 sad path — the escape-sequence value is neutralized the same way: the boot
        /// line reads exactly "...GW_APP_VERSION=invalid".</summary>
        [Fact]
        public void LogsInvalid() =>
            Assert.Contains(
                "[engine-entrypoint] control-plane version: GW_APP_VERSION=invalid",
                arc.StdErrLines);
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

// ── Arc: AC4's engine-config body, fetched once ──────────────────────────────────────────────────

/// <summary>
/// Arranges ONE <see cref="SchemaDriftWebFactory"/> boot — reused rather than duplicated, since its
/// IAppVersion is already stamped "5.14.0+abc1234" for the /api/status scenarios above — and fetches
/// GET /internal/engine-config once, splitting the body into lines for AC4's two read-only facts.
/// No login: the group is AllowAnonymous (<see cref="InternalEndpoints"/>'s own remarks), unlike
/// the /api/status arc above.
/// </summary>
public sealed class EngineConfigArc : IAsyncLifetime
{
    public IReadOnlyList<string> Lines { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await using var factory = new SchemaDriftWebFactory(new FakeSchemaJournal(applied: SchemaVersion.Expected));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/internal/engine-config");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Lines = body.TrimEnd('\n').Split('\n');
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Arc: AC5's engine/entrypoint.sh run, once per GW_APP_VERSION value under test ────────────────

/// <summary>
/// Runs the REAL <c>engine/entrypoint.sh</c> once through <see cref="ScriptProcess"/> (gh-#776)
/// against a curl stub serving <paramref name="curlAppVersionArg"/> as the fourth engine-config
/// line — entrypoint.sh's final <c>exec liquidsoap /genwave.liq</c> would otherwise replace this
/// process with the real audio engine, which is neither installed nor wanted in a unit-test run.
/// The liquidsoap stub instead records its own argv and environment to a scratch file before
/// exiting 0 — the runtime proof <see cref="FeatureVersionMarkers.ScenarioTheEngineEntrypoint"/>'s
/// "nothing else reads it" facts assert against, replacing a prior static scan of entrypoint.sh's
/// own source (a scan can't tell whether a value that PASSES the parse arm ever actually reaches
/// Liquidsoap; a recorded exec can). Also reads genwave.liq's own source once, for the cheap text
/// fact that rounds out "nothing else reads it": the script Liquidsoap actually runs never names
/// the key either.
///
/// Runs entrypoint.sh via <see cref="ScriptProcess.RunWithEmptyEnvironment"/>, not <see
/// cref="ScriptProcess.Run"/>: the argv/env facts this arc feeds read the liquidsoap stub's OWN
/// recorded environment byte-for-byte, so any variable merely PASSED THROUGH from the test
/// process (a CI runner's <c>GITHUB_HEAD_REF</c>, say — a branch name can itself contain the
/// version substring under test) would otherwise read as a false leak. An empty starting
/// environment makes the fact describe entrypoint.sh's OWN behaviour only.
/// </summary>
public abstract class EntrypointRunArc(string curlAppVersionArg) : IAsyncLifetime
{
    public string GenwaveLiqText { get; private set; } = "";

    public IReadOnlyList<string> StdErrLines { get; private set; } = [];

    /// <summary>The liquidsoap stub's own recorded argv (one <c>printf '%s\n' "$@"</c> line per
    /// argument) followed by its recorded <c>env</c> dump — what Liquidsoap itself actually saw at
    /// exec time.</summary>
    public string LiquidsoapArgsAndEnv { get; private set; } = "";

    public Task InitializeAsync()
    {
        var repoRoot = RepoRootLocator.Find(AppContext.BaseDirectory);
        GenwaveLiqText = File.ReadAllText(Path.Combine(repoRoot, "engine", "genwave.liq"));

        // EntrypointHarness owns the scratch PATH + liquidsoap-recording stub (shared with Story487).
        var bin = EntrypointHarness.MakeBinDirWithLiquidsoapStub(out var recorded);
        ScriptProcess.AddStub(bin, "curl", $"""
            printf '%s\n' 'GW_XFADE_MIN=2' 'GW_XFADE_MAX=8' 'GW_SAFE_GAP_SECONDS=7' {curlAppVersionArg}
            exit 0
            """);

        StdErrLines = EntrypointHarness.RunOrThrow(bin).Split('\n');
        LiquidsoapArgsAndEnv = File.ReadAllText(recorded);

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>Happy path — engine-config serves a well-formed GW_APP_VERSION.</summary>
public sealed class EntrypointArc() : EntrypointRunArc("'GW_APP_VERSION=v5.14.0'")
{
}

/// <summary>AC5 sad path — engine-config serves a GW_APP_VERSION with an embedded raw CR followed
/// by a forged log line. The C# <c>\r</c> escape below places one literal CR byte inside the bash
/// single-quoted token (single quotes pass every byte through untouched, so no further quoting
/// gymnastics are needed to get a raw CR onto the wire).</summary>
public sealed class ForgedCrEntrypointArc() : EntrypointRunArc(
    "'GW_APP_VERSION=v1.0\r[engine-entrypoint] forged'")
{
}

/// <summary>AC5 sad path — engine-config serves a GW_APP_VERSION with a raw ANSI escape (colour
/// code) embedded in it, the other classic terminal-log-injection payload shape alongside a raw
/// CR.</summary>
public sealed class AnsiEscapeEntrypointArc() : EntrypointRunArc(
    "'GW_APP_VERSION=v1.0\u001b[31mred'")
{
}
