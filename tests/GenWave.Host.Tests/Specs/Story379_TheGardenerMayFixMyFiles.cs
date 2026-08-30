// STORY-379 — The gardener may fix my files, when I say so (SPEC F154 · PLAN T379/T380/T381)
//
// BDD specification — xUnit. WIRED at T381: every fact drives the REAL production binary
// (WebApplicationFactory<Program>, the Story345/Story366 factory idiom over the ephemeral Postgres —
// Support/EphemeralStationDatabase) — POST /api/gardener/file-actions/dry-run and …/confirm,
// AdminOnly, Gardener__FileActions__Enabled=true for every scenario except AC1 (which needs it
// unset). Arrange: a fresh temp directory per scenario as the library root (Library:MediaRoot), with
// a real ffmpeg-authored small mp3 fixture inside it (the Story016/Gh257 idiom — TagLib needs a
// genuine frame to retag) plus a matching media row; a second temp directory stands in as a second
// library's root and a third as an exempt root (Library:Scan:QuarantineExemptRoots) for AC11; a real
// filesystem symlink inside the root pointing outside it for AC10 — never a mocked filesystem, since
// the jail's own canonicalise/symlink-resolve/root-prefix check is the thing under test. A real scan
// tick (ScanService) runs after confirm for AC3's own zero-drift claim, in its own isolated
// ephemeral Postgres/MediaRoot (ScanZeroDriftArc) — no other scenario's fixture rows are anywhere
// near a real, ticking ScanService, so a scan can never misjudge them.
//
// AC2/AC4-AC7/AC9-AC14 share ONE ephemeral Postgres/WebApplicationFactory (FileActionsArc, hosted
// services removed — the same "direct" idiom Story374's own DeadFileLifecycleArc establishes),
// each scenario its own subdirectory/media row so nothing collides. AC14's expired-token fact reuses
// that SAME arc's own injected Microsoft.Extensions.Time.Testing.FakeTimeProvider (already a
// referenced test package) — advanced past the 10-minute plan horizon ONLY as this arc's LAST step,
// so no earlier scenario's own token mint/confirm pair is affected. AC1 (disabled) and AC8
// (AdminOnly, not merely authenticated — see ScenarioAdminOnly's own remarks for why gh-#8's single
// shared admin password needs a DI-level policy override to prove this) are DB-less factories (the
// Story374 GardenerSurfaceWebFactory idiom) — neither ever reaches the database.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using GenWave.Core.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheGardenerMayFixMyFiles
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — dry-run plans, confirm executes, the audit and admin gate hold
    // ---------------------------------------------------------------------

    public sealed class ScenarioDefaultOff
    {
        // Given Gardener:FileActions:Enabled unset, When POST /api/gardener/file-actions/dry-run is called.
        [Fact]
        public async Task TheResponseIsFourOhFour()
        {
            await using var factory = new DisabledFileActionsWebFactory();
            var client = await FileActionsTestHarness.LoggedInClientAsync(factory, DisabledFileActionsWebFactory.Password);

            var response = await FileActionsTestHarness.DryRunAsync(client, 1, "rename");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task TheResponseNamesTheEnablingKnob()
        {
            await using var factory = new DisabledFileActionsWebFactory();
            var client = await FileActionsTestHarness.LoggedInClientAsync(factory, DisabledFileActionsWebFactory.Password);

            var response = await FileActionsTestHarness.DryRunAsync(client, 1, "rename");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("Gardener:FileActions:Enabled", body, StringComparison.Ordinal);
        }
    }

    [Collection(FileActionsCollection.Name)]
    public sealed class ScenarioDryRunReturnsAPlanAndAToken(FileActionsArc arc)
    {
        // Given actions enabled and media at .../ac2/x.mp3, When dry-run {mediaId, verb: "rename"} is called.
        [Fact]
        public void TheResponseCarriesTheFromPath() => Assert.Equal(arc.RenameSubjectPath, arc.RenamePlan.From);

        [Fact]
        public void TheResponseCarriesTheComputedToPath() => Assert.Equal(arc.RenameComputedTo, arc.RenamePlan.To);

        [Fact]
        public void TheResponseCarriesAPlanToken() => Assert.False(string.IsNullOrEmpty(arc.RenamePlan.PlanToken));
    }

    public sealed class ScenarioConfirmExecutesRenameAndReStamps(ScanZeroDriftArc arc) : IClassFixture<ScanZeroDriftArc>
    {
        // Given the plan above, When confirm {plan_token} is called.
        [Fact]
        public void TheFileIsAtTheNewPath() => Assert.True(arc.FileExistsAtNewPath);

        [Fact]
        public void TheLibraryRowsPathSizeAndMtimeMatchIt() => Assert.True(arc.RowMatchesFileAfterConfirm);

        [Fact]
        public void TheNextScanReportsZeroDiscoveredChangedOrMissing() => Assert.True(arc.ScanRanAndRenamedRowUnchanged);
    }

    [Collection(FileActionsCollection.Name)]
    public sealed class ScenarioRetagWritesTagsNotAudio(FileActionsArc arc)
    {
        // Given media with catalog artist "Cat Artist" and file tag artist "File Artist", When retag is confirmed.
        [Fact]
        public void TheFilesArtistTagIsA() => Assert.Equal("Cat Artist", arc.FileArtistTagAfterRetag);

        [Fact]
        public void TheAudioStreamBytesAreUnchanged() => Assert.Equal(arc.AudioMd5BeforeRetag, arc.AudioMd5AfterRetag);

        [Fact]
        public void TheEnrichmentStampsAreUntouched() => Assert.Equal(arc.EnrichmentBeforeRetag, arc.EnrichmentAfterRetag);
    }

    [Collection(FileActionsCollection.Name)]
    public sealed class ScenarioMoveWithinTheRoot(FileActionsArc arc)
    {
        // Given a target directory under the same library root, When move is confirmed.
        [Fact]
        public void TheFileIsAtTheTargetDirectory() => Assert.True(arc.MoveFileExistsAtTarget);

        [Fact]
        public void TheRowsPathFollowsIt() => Assert.Equal(arc.MoveTargetPath, arc.MoveRowPathAfterConfirm);
    }

    [Collection(FileActionsCollection.Name)]
    public sealed class ScenarioTheAudit(FileActionsArc arc)
    {
        // Given the confirmed move above, When library.file_action is read.
        [Fact]
        public void OneRowCarriesVerbFromToPlanTokenAndOutcome() => Assert.Equal(
            ("move", arc.MoveSubjectPath, arc.MoveTargetPath, arc.MoveConfirmToken, "done"),
            (arc.MoveAuditRow.Verb, arc.MoveAuditRow.FromPath, arc.MoveAuditRow.ToPath, arc.MoveAuditRow.PlanToken, arc.MoveAuditRow.Outcome));
    }

    [Collection(FileActionsCollection.Name)]
    public sealed class ScenarioToctou(FileActionsArc arc)
    {
        // Given a plan token minted before the row was PATCHed (xmin changed), When confirm is called.
        [Fact]
        public void TheResponseIsFourOhNine() => Assert.Equal(HttpStatusCode.Conflict, arc.ToctouConfirmStatus);

        [Fact]
        public void NothingMoved() => Assert.True(arc.ToctouFileStillAtOriginalPath);
    }

    public sealed class ScenarioAdminOnly
    {
        // Given a session authenticated under the shared admin login, but this endpoint's own
        // AdminOnly policy specifically neutralized (gh-#8's own remarks: every admin-plane policy
        // carries the SAME AdminOnlyRequirement today, so a genuinely "Curation-only, not
        // AdminOnly" session cannot be produced through the real login flow — this instead proves
        // the endpoint's own gate really is policy-checked, the same "authenticated but forbidden"
        // shape a real RBAC split would eventually produce), When dry-run is called.
        [Fact]
        public async Task TheResponseIsFourOhThree()
        {
            await using var factory = new DenyAdminOnlyWebFactory();
            var client = await FileActionsTestHarness.LoggedInClientAsync(factory, DenyAdminOnlyWebFactory.Password);

            var response = await FileActionsTestHarness.DryRunAsync(client, 1, "rename");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the jail refuses, and failures never half-do a move
    // ---------------------------------------------------------------------

    [Collection(FileActionsCollection.Name)]
    public sealed class ScenarioTraversalIsRefused(FileActionsArc arc)
    {
        // Given a target of "../../etc/x.mp3", When dry-run is called.
        [Fact]
        public void TheResponseIsFourHundredNamingTheRule()
        {
            Assert.Equal(HttpStatusCode.BadRequest, arc.TraversalStatus);
            Assert.Contains("traversal", arc.TraversalBody, StringComparison.Ordinal);
        }

        [Fact]
        public void TheOffendingPathIsNotEchoed() =>
            Assert.DoesNotContain("../../etc/x.mp3", arc.TraversalBody, StringComparison.Ordinal);
    }

    [Collection(FileActionsCollection.Name)]
    public sealed class ScenarioASymlinkEscapeIsRefused(FileActionsArc arc)
    {
        // Given a directory under the root that is a symlink to outside it, When move targets it.
        [Fact]
        public void TheResponseIsFourHundred() => Assert.Equal(HttpStatusCode.BadRequest, arc.SymlinkEscapeStatus);

        [Fact]
        public void TheRefusalHappensBeforeAnyIo() => Assert.True(arc.SymlinkEscapeSubjectUntouched);
    }

    [Collection(FileActionsCollection.Name)]
    public sealed class ScenarioAnotherRootIsRefused(FileActionsArc arc)
    {
        // Given a target under a different library's root or under an exempt root, When dry-run is called.
        [Fact]
        public void TheDifferentLibraryRootTargetIsFourHundred() =>
            Assert.Equal(HttpStatusCode.BadRequest, arc.OutsideRootStatus);

        [Fact]
        public void TheExemptRootTargetIsFourHundred() => Assert.Equal(HttpStatusCode.BadRequest, arc.ExemptRootStatus);
    }

    [Collection(FileActionsCollection.Name)]
    public sealed class ScenarioNeverOverwrite(FileActionsArc arc)
    {
        // Given a target that exists (appeared between dry-run and confirm), When confirm is called.
        [Fact]
        public void TheResponseIsFourOhNine() => Assert.Equal(HttpStatusCode.Conflict, arc.NeverOverwriteConfirmStatus);

        [Fact]
        public void BothFilesAreUnchanged() => Assert.True(arc.NeverOverwriteBothFilesUnchanged);
    }

    [Collection(FileActionsCollection.Name)]
    public sealed class ScenarioAFailedDbUpdateRevertsTheMove(FileActionsArc arc)
    {
        // Given the FS move succeeds and the row update throws, When confirm runs.
        [Fact]
        public void TheFileIsBackAtTheOriginalPath() => Assert.True(arc.RevertFileBackAtOriginalPath);

        [Fact]
        public void TheAuditRowSaysReverted() => Assert.Equal("reverted", arc.RevertAuditOutcome);
    }

    [Collection(FileActionsCollection.Name)]
    public sealed class ScenarioAnExpiredToken(FileActionsArc arc)
    {
        // Given a plan token older than 10 minutes, When confirm is called.
        [Fact]
        public void TheResponseIsFourOhNine() => Assert.Equal(HttpStatusCode.Conflict, arc.ExpiredTokenConfirmStatus);
    }

    // ---------------------------------------------------------------------
    // N7 — additional wire facts the 14 STORY-379 ACs don't individually name
    // ---------------------------------------------------------------------

    [Collection(FileActionsCollection.Name)]
    public sealed class ScenarioDryRunTargetExistsIsRefused(FileActionsArc arc)
    {
        // Given a rename target that already exists BEFORE dry-run is ever called, When dry-run runs.
        [Fact]
        public void TheResponseIsFourOhNine() => Assert.Equal(HttpStatusCode.Conflict, arc.DryRunTargetExistsStatus);
    }

    [Collection(FileActionsCollection.Name)]
    public sealed class ScenarioATamperedTokenIsRefused(FileActionsArc arc)
    {
        // Given a valid plan token with one character flipped, When confirm is called.
        [Fact]
        public void TheResponseIsFourOhNine() => Assert.Equal(HttpStatusCode.Conflict, arc.TamperedTokenConfirmStatus);
    }

    [Collection(FileActionsCollection.Name)]
    public sealed class ScenarioAnonymousIsDeniedOnBothRoutes(FileActionsArc arc)
    {
        // Given no session at all, When dry-run is called.
        [Fact]
        public void DryRunIsFourOhOne() => Assert.Equal(HttpStatusCode.Unauthorized, arc.AnonymousDryRunStatus);

        // Given no session at all, When confirm is called.
        [Fact]
        public void ConfirmIsFourOhOne() => Assert.Equal(HttpStatusCode.Unauthorized, arc.AnonymousConfirmStatus);
    }

    [Collection(FileActionsCollection.Name)]
    public sealed class ScenarioUnknownVerbIsRejected(FileActionsArc arc)
    {
        // Given verb "obliterate", When dry-run is called.
        [Fact]
        public void TheResponseIsFourHundred() => Assert.Equal(HttpStatusCode.BadRequest, arc.UnknownVerbStatus);

        [Fact]
        public void TheUnknownVerbIsNotEchoed() =>
            Assert.DoesNotContain("obliterate", arc.UnknownVerbBody, StringComparison.Ordinal);
    }

    [Collection(FileActionsCollection.Name)]
    public sealed class ScenarioUnknownMediaIdIsNotFound(FileActionsArc arc)
    {
        // Given a mediaId with no matching row, When dry-run is called.
        [Fact]
        public void TheResponseIsFourOhFour() => Assert.Equal(HttpStatusCode.NotFound, arc.UnknownMediaIdStatus);

        [Fact]
        public void TheIdIsNotEchoed() =>
            Assert.DoesNotContain("987654321", arc.UnknownMediaIdBody, StringComparison.Ordinal);
    }

    [Collection(FileActionsCollection.Name)]
    public sealed class ScenarioBusyWhileScanGateIsHeld(FileActionsArc arc)
    {
        // Given the shared IScanGate held for the whole attempt, When confirm is called.
        [Fact]
        public void TheResponseIsFiveOhThree() => Assert.Equal(HttpStatusCode.ServiceUnavailable, arc.BusyConfirmStatus);

        [Fact]
        public void RetryAfterIsThirtySeconds() => Assert.Equal(TimeSpan.FromSeconds(30), arc.BusyRetryAfter);
    }

    [Collection(FileActionsCollection.Name)]
    public sealed class ScenarioLeftoverBackupIsRefused(FileActionsArc arc)
    {
        // Given a pre-existing *.gwbak sibling, When a retag is confirmed.
        [Fact]
        public void TheResponseIsFourOhNine() => Assert.Equal(HttpStatusCode.Conflict, arc.LeftoverBackupConfirmStatus);

        [Fact]
        public void TheMessageMentionsTheBackup() =>
            Assert.Contains("backup", arc.LeftoverBackupMessage, StringComparison.OrdinalIgnoreCase);
    }
}

// ── Collection definitions ──────────────────────────────────────────────────────────────────────

[CollectionDefinition(Name)]
public sealed class FileActionsCollection : ICollectionFixture<FileActionsArc>
{
    public const string Name = "Story381FileActions";
}

// ── Shared HTTP/DB helpers ───────────────────────────────────────────────────────────────────────

/// <summary>Small, file-local record shape for one <c>library.file_action</c> row — the fields
/// STORY-379 AC6 actually asserts, mirroring <c>FileActionAuditRecord</c>'s own shape without
/// reaching into MediaLibrary internals.</summary>
public readonly record struct FileActionAuditRow(string Verb, string? FromPath, string? ToPath, string PlanToken, string Outcome, string Detail);

/// <summary>The dry-run 200 body's own shape, read back off the wire — never the production
/// <c>FileActionPlan</c> type (this project has no reference to it).</summary>
public readonly record struct FileActionPlanWire(string From, string To, string PlanToken, string ExpiresAt);

public static class FileActionsTestHarness
{
    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory, string password)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");
        return client;
    }

    public static Task<HttpResponseMessage> DryRunAsync(HttpClient client, long mediaId, string verb, string? target = null) =>
        client.PostAsJsonAsync("/api/gardener/file-actions/dry-run", new { mediaId, verb, target });

    public static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string planToken) =>
        client.PostAsJsonAsync("/api/gardener/file-actions/confirm", new { planToken });

    public static async Task<FileActionPlanWire> ReadPlanAsync(HttpResponseMessage response)
    {
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return new FileActionPlanWire(
            root.GetProperty("from").GetString() ?? "",
            root.GetProperty("to").GetString() ?? "",
            root.GetProperty("planToken").GetString() ?? "",
            root.GetProperty("expiresAt").GetString() ?? "");
    }

    public static async Task<string> ReadOutcomeAsync(HttpResponseMessage response)
    {
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return root.TryGetProperty("outcome", out var outcome) ? outcome.GetString() ?? "" : "";
    }

    /// <summary>B2's own real predicate-poll (replacing an unconditional sleep) — the SAME
    /// wait-with-poll idiom <c>GardenerRotFixtures.WaitUntilAsync</c> establishes one file over,
    /// generalised to an async predicate so a caller can poll a database read. Throws (never
    /// silently returns) on timeout — a caller waiting for a sentinel that never lands is an arrange
    /// failure, not a fact this suite should quietly mis-report as false.</summary>
    public static async Task WaitUntilAsync(Func<Task<bool>> predicate, TimeSpan timeout, string timeoutMessage)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new InvalidOperationException(timeoutMessage);
    }
}

/// <summary>Raw SQL arrange/read helpers, independent of any production repository — the same
/// "independent read of what actually landed" posture <c>GardenerRotFixtures</c> already
/// establishes one file over.</summary>
public static class FileActionDbFixtures
{
    /// <summary>Mirrors <c>Scan.ScanMtime.TruncateToSeconds</c> (internal to <c>GenWave.MediaLibrary</c>,
    /// no <c>InternalsVisibleTo</c> reaches this project) — see <see cref="InsertMediaRowAsync"/>'s
    /// own remarks for why an inserted row's <c>mtime</c> must already be truncated this way.</summary>
    static DateTime TruncateToSeconds(DateTime t) => new(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    public static async Task<long> InsertMediaRowAsync(
        string libraryConnectionString, string filePath,
        string? artist, string? title, string? album, string? genre, int? year)
    {
        var info = new FileInfo(filePath);
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into library.media (path, format, size_bytes, mtime, state, artist, title, album, genre, year)
            values (@path, 'mp3', @sizeBytes, @mtime, 'ready', @artist, @title, @album, @genre, @year)
            returning id
            """;
        cmd.Parameters.AddWithValue("path", filePath);
        cmd.Parameters.AddWithValue("sizeBytes", info.Length);
        // Truncated to whole seconds — the SAME Scan.ScanMtime.TruncateToSeconds precision every
        // real scan/file-action write already uses (that type's own remarks: "a stat round-trips
        // through timestamptz exactly and never spuriously re-triggers a scan's own 'changed'
        // classification"). An un-truncated insert here would make ScanZeroDriftArc's real scan
        // tick see THIS row as already "changed" before the rename under test ever runs.
        cmd.Parameters.AddWithValue("mtime", TruncateToSeconds(info.LastWriteTimeUtc));
        cmd.Parameters.AddWithValue("artist", (object?)artist ?? DBNull.Value);
        cmd.Parameters.AddWithValue("title", (object?)title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("album", (object?)album ?? DBNull.Value);
        cmd.Parameters.AddWithValue("genre", (object?)genre ?? DBNull.Value);
        cmd.Parameters.AddWithValue("year", (object?)year ?? DBNull.Value);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }

    public static async Task<(string Path, long SizeBytes, DateTimeOffset Mtime, string State)> ReadMediaRowAsync(
        string libraryConnectionString, long mediaId)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select path, size_bytes, mtime, state from library.media where id = @mediaId";
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException($"no library.media row for id {mediaId}");
        return (reader.GetString(0), reader.GetInt64(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3));
    }

    /// <summary>The whole table's own row count — B2's own sentinel proof: a genuine scan tick
    /// discovering a freshly-dropped file is the ONE repository-visible effect this suite can poll
    /// for without reaching into ScanService's own internals (internal, no InternalsVisibleTo here).
    /// </summary>
    public static async Task<long> CountMediaRowsAsync(string libraryConnectionString)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*) from library.media";
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("count(*) returned no row"));
    }

    public static async Task TouchTitleAsync(string libraryConnectionString, long mediaId, string newTitle)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "update library.media set title = @title where id = @mediaId";
        cmd.Parameters.AddWithValue("title", newTitle);
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task SetEnrichmentAsync(string libraryConnectionString, long mediaId)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            update library.media
            set integrated_lufs = -14.0, true_peak_dbtp = -1.0, measurable = true,
                cue_in_sec = 0.05, cue_out_sec = 0.8, bpm = 120.0
            where id = @mediaId
            """;
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<string> ReadEnrichmentFingerprintAsync(string libraryConnectionString, long mediaId)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "select integrated_lufs, true_peak_dbtp, measurable, cue_in_sec, cue_out_sec, bpm from library.media where id = @mediaId";
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException($"no library.media row for id {mediaId}");
        var values = new object?[6];
        reader.GetValues(values!);
        return string.Join('|', values.Select(v => v?.ToString() ?? "null"));
    }

    public static async Task<FileActionAuditRow> ReadLatestAuditAsync(string libraryConnectionString, long mediaId)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            select verb::text, from_path, to_path, plan_token, outcome, detail::text
            from library.file_action
            where media_id = @mediaId
            order by performed_at desc
            limit 1
            """;
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException($"no library.file_action row for media {mediaId}");
        return new FileActionAuditRow(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5));
    }

    /// <summary>T380's own trigger technique (Story379_FileActionExecutors.cs's own
    /// <c>ArmFailingTriggerAsync</c>), reused verbatim at the Host level (no shared test-support
    /// project spans both assemblies) — a <c>before update</c> trigger scoped to ONE row that always
    /// raises, forcing <c>FileActionRepository.RelocateAsync</c>'s own UPDATE to fail so the executor
    /// reverts.</summary>
    public static async Task ArmFailingTriggerAsync(string libraryConnectionString, string triggerName, long mediaId)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"""
            create or replace function library.{triggerName}() returns trigger
            language plpgsql as $trig$
            begin
              raise exception 'T381 test-induced failure';
            end;
            $trig$;

            drop trigger if exists {triggerName} on library.media;
            create trigger {triggerName}
              before update on library.media
              for each row
              when (old.id = {mediaId})
              execute function library.{triggerName}();
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task DisarmTriggerAsync(string libraryConnectionString, string triggerName)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"drop trigger if exists {triggerName} on library.media;";
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>Generates a tiny real mp3 via ffmpeg (the Story016/Gh257 idiom — TagLib needs a genuine
/// frame to retag) and computes the audio STREAM's own content hash. File-local per this file's own
/// "no shared test-support project" precedent (TestMedia.cs's own header, GenWave.MediaLibrary.Tests).</summary>
public static class FileActionTone
{
    public static string NewTempDir(string suffix)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"gw-hosttest-{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string Create(
        string dir, string fileName, string? artist = null, string? title = null, string? album = null,
        string? genre = null, int? year = null)
    {
        var path = Path.Combine(dir, fileName);
        var args = new List<string>
        {
            "-nostats", "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
            "-ar", "44100", "-ac", "2",
        };

        void Meta(string key, string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            args.Add("-metadata");
            args.Add($"{key}={value}");
        }

        Meta("artist", artist);
        Meta("title", title);
        Meta("album", album);
        Meta("genre", genre);
        if (year is not null) Meta("date", year.Value.ToString(CultureInfo.InvariantCulture));

        args.Add(path);
        RunFfmpeg(args);
        return path;
    }

    /// <summary>The audio STREAM's own content hash — <c>-map 0:a -c copy</c> never re-encodes, so
    /// this changes if and only if the audio bytes themselves changed (a tag-only rewrite leaves it
    /// identical) — the same AC4 proof <c>Story379_FileActionExecutors.cs</c>'s own <c>AudioMd5</c>
    /// establishes at the MediaLibrary level.</summary>
    public static string AudioMd5(string path)
    {
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "-nostats", "-hide_banner", "-loglevel", "error", "-i", path, "-map", "0:a", "-c", "copy", "-f", "md5", "-" })
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to start ffmpeg");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"ffmpeg md5 failed: {stderr.Result}");
        return stdout.Result.Trim();
    }

    static void RunFfmpeg(IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to start ffmpeg");
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"ffmpeg failed: {stderr.Result}");
    }
}

// ── Test harness — WebApplicationFactory subclasses ─────────────────────────────────────────────

/// <summary>AC1's own DB-less factory (mirrors Story374's <c>GardenerSurfaceWebFactory</c> — the
/// disabled check is the endpoint's own FIRST statement, so a bogus connection string never gets
/// touched). <c>Gardener:FileActions:Enabled</c> is deliberately left UNSET (default
/// <see langword="false"/>).</summary>
file sealed class DisabledFileActionsWebFactory : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-t381-disabled";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
    }
}

/// <summary>AC8's own DB-less factory — see <c>ScenarioAdminOnly</c>'s own remarks for why this
/// neutralizes ONLY the "AdminOnly" named policy (every other admin-plane name, including
/// "Curation", is left exactly as production registers it).</summary>
file sealed class DenyAdminOnlyWebFactory : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-t381-deny-admin-only";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");
        builder.UseSetting("Gardener:FileActions:Enabled", "true");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.Configure<AuthorizationOptions>(options =>
                options.AddPolicy(AuthorizationPolicies.AdminOnly, policy => policy.RequireAssertion(_ => false)));
        });
    }
}

/// <summary>The shared arc's own factory — hosted services removed (no real scan/enrichment loop
/// reach, mirrors Story374's <c>Story372DirectPassWebFactory</c>), a controllable
/// <see cref="FakeTimeProvider"/> REPLACING the container's default <c>TimeProvider.System</c> (AC14
/// advances it as this arc's own LAST step), and the exempt root wired for AC11.</summary>
file sealed class FileActionsWebFactory(
    FileActionsDatabase db, string mediaRoot, string exemptRoot, FakeTimeProvider clock) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-t381-file-actions";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");
        builder.UseSetting("Library:MediaRoot", mediaRoot);
        builder.UseSetting("Library:Scan:QuarantineExemptRoots:0", exemptRoot);
        builder.UseSetting("Gardener:FileActions:Enabled", "true");
        // N7's own Busy fact holds IScanGate for the duration of one confirm call — floored to 1s
        // (GardenerFileActionsOptions' own [1, 300] range) so that fact times out fast for real,
        // rather than faking the outcome or waiting out the 30s production default. Harmless to
        // every OTHER scenario in this arc: none of them ever contend for the gate.
        builder.UseSetting("Gardener:FileActions:GateTimeoutSeconds", "1");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
    }
}

/// <summary>AC3's own isolated factory — keeps the REAL, container-composed <c>ScanService</c> alive
/// by NAME (captured before <c>RemoveAll&lt;IHostedService&gt;()</c> and re-added — Story372
/// LiveServiceWebFactory's own idiom, GardenerService retargeted to ScanService here), a short
/// <c>Library:ScanIntervalSeconds</c> so a real second tick happens within this fact's own wait
/// budget.</summary>
file sealed class ScanZeroDriftWebFactory(ScanZeroDriftDatabase db, string mediaRoot) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-t381-scan-zero-drift";

    /// <summary>B2: how many <c>IHostedService</c> descriptors the by-name filter below actually
    /// matched — the Arc asserts this is at least 1 BEFORE trusting anything downstream of it. A
    /// silent 0 (e.g. ScanService renamed, or moved to a different registration shape) would
    /// otherwise make this whole factory a no-op scan, and every fact built on it would misreport
    /// "zero drift" for the wrong reason (nothing ever ran, not "ran and agreed").</summary>
    public int ScanDescriptorCount { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");
        builder.UseSetting("Library:MediaRoot", mediaRoot);
        builder.UseSetting("Library:ScanIntervalSeconds", "2");
        builder.UseSetting("Gardener:FileActions:Enabled", "true");

        builder.ConfigureTestServices(services =>
        {
            var scanDescriptors = services
                .Where(sd => sd.ServiceType == typeof(IHostedService) && sd.ImplementationType?.Name == "ScanService")
                .ToList();
            ScanDescriptorCount = scanDescriptors.Count;

            services.RemoveAll<IHostedService>();
            foreach (var descriptor in scanDescriptors)
                services.Add(descriptor);
        });
    }
}

file sealed class FileActionsDatabase : EphemeralStationDatabase
{
    FileActionsDatabase(string project, string composeFile, string library, string station)
        : base(project, composeFile, library, station)
    {
    }

    public static async Task<FileActionsDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t381a");
        var db = new FileActionsDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

file sealed class ScanZeroDriftDatabase : EphemeralStationDatabase
{
    ScanZeroDriftDatabase(string project, string composeFile, string library, string station)
        : base(project, composeFile, library, station)
    {
    }

    public static async Task<ScanZeroDriftDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t381b");
        var db = new ScanZeroDriftDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

// ── Arc fixtures ─────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// AC2, AC4-AC7, AC9-AC14: one ephemeral Postgres/WebApplicationFactory (hosted services removed),
/// one media root, one subdirectory + real mp3 + media row per scenario so nothing collides. Every
/// step drives the REAL dry-run/confirm endpoints over HTTP; DB/filesystem reads afterward are
/// independent of the production repository (never "the repository agrees with itself").
/// </summary>
public sealed class FileActionsArc : IAsyncLifetime
{
    // AC2
    public string RenameSubjectPath { get; private set; } = "";
    public string RenameComputedTo { get; private set; } = "";
    public FileActionPlanWire RenamePlan { get; private set; }

    // AC4
    public string FileArtistTagAfterRetag { get; private set; } = "";
    public string AudioMd5BeforeRetag { get; private set; } = "";
    public string AudioMd5AfterRetag { get; private set; } = "";
    public string EnrichmentBeforeRetag { get; private set; } = "";
    public string EnrichmentAfterRetag { get; private set; } = "";

    // AC5 / AC6
    public bool MoveFileExistsAtTarget { get; private set; }
    public string MoveTargetPath { get; private set; } = "";
    public string MoveRowPathAfterConfirm { get; private set; } = "";
    public string MoveSubjectPath { get; private set; } = "";
    public string MoveConfirmToken { get; private set; } = "";
    public FileActionAuditRow MoveAuditRow { get; private set; }

    // AC7
    public HttpStatusCode ToctouConfirmStatus { get; private set; }
    public bool ToctouFileStillAtOriginalPath { get; private set; }

    // AC9
    public HttpStatusCode TraversalStatus { get; private set; }
    public string TraversalBody { get; private set; } = "";

    // AC10
    public HttpStatusCode SymlinkEscapeStatus { get; private set; }
    public bool SymlinkEscapeSubjectUntouched { get; private set; }

    // AC11
    public HttpStatusCode OutsideRootStatus { get; private set; }
    public HttpStatusCode ExemptRootStatus { get; private set; }

    // AC12
    public HttpStatusCode NeverOverwriteConfirmStatus { get; private set; }
    public bool NeverOverwriteBothFilesUnchanged { get; private set; }

    // AC13
    public bool RevertFileBackAtOriginalPath { get; private set; }
    public string RevertAuditOutcome { get; private set; } = "";

    // AC14
    public HttpStatusCode ExpiredTokenConfirmStatus { get; private set; }

    // N7 — additional wire facts beyond the 14 ACs
    public HttpStatusCode DryRunTargetExistsStatus { get; private set; }
    public HttpStatusCode TamperedTokenConfirmStatus { get; private set; }
    public HttpStatusCode AnonymousDryRunStatus { get; private set; }
    public HttpStatusCode AnonymousConfirmStatus { get; private set; }
    public HttpStatusCode UnknownVerbStatus { get; private set; }
    public string UnknownVerbBody { get; private set; } = "";
    public HttpStatusCode UnknownMediaIdStatus { get; private set; }
    public string UnknownMediaIdBody { get; private set; } = "";
    public HttpStatusCode BusyConfirmStatus { get; private set; }
    public TimeSpan? BusyRetryAfter { get; private set; }
    public HttpStatusCode LeftoverBackupConfirmStatus { get; private set; }
    public string LeftoverBackupMessage { get; private set; } = "";

    readonly List<string> tempDirs = [];

    public async Task InitializeAsync()
    {
        await using var database = await FileActionsDatabase.StartAsync();

        var root = FileActionTone.NewTempDir("t381-root");
        var exemptRoot = FileActionTone.NewTempDir("t381-exempt");
        var outsideRoot = FileActionTone.NewTempDir("t381-outside");
        var anotherRootDir = FileActionTone.NewTempDir("t381-another-root");
        tempDirs.AddRange([root, exemptRoot, outsideRoot, anotherRootDir]);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        await using var factory = new FileActionsWebFactory(database, root, exemptRoot, clock);
        var client = await FileActionsTestHarness.LoggedInClientAsync(factory, FileActionsWebFactory.Password);

        // ── AC2 — dry-run rename returns a plan and a token ──────────────────────────────────
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "ac2")).FullName;
            var path = FileActionTone.Create(dir, "x.mp3");
            var mediaId = await FileActionDbFixtures.InsertMediaRowAsync(
                database.LibraryConnectionString, path, "Artist A", "Title T", null, null, null);

            var response = await FileActionsTestHarness.DryRunAsync(client, mediaId, "rename");
            if (response.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"AC2 arrange: dry-run returned {response.StatusCode}");

            var plan = await FileActionsTestHarness.ReadPlanAsync(response);
            RenameSubjectPath = path;
            RenameComputedTo = Path.Combine(dir, "Artist A - Title T.mp3");
            RenamePlan = plan;
        }

        // ── AC4 — retag writes tags, not audio, and leaves enrichment untouched ──────────────
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "ac4")).FullName;
            var path = FileActionTone.Create(
                dir, "x.mp3", artist: "File Artist", title: "Same Title", album: "Same Album", genre: "Same Genre", year: 2020);
            var mediaId = await FileActionDbFixtures.InsertMediaRowAsync(
                database.LibraryConnectionString, path, "Cat Artist", "Same Title", "Same Album", "Same Genre", 2020);
            await FileActionDbFixtures.SetEnrichmentAsync(database.LibraryConnectionString, mediaId);

            AudioMd5BeforeRetag = FileActionTone.AudioMd5(path);
            EnrichmentBeforeRetag = await FileActionDbFixtures.ReadEnrichmentFingerprintAsync(database.LibraryConnectionString, mediaId);

            var dryRun = await FileActionsTestHarness.DryRunAsync(client, mediaId, "retag");
            if (dryRun.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"AC4 arrange: dry-run returned {dryRun.StatusCode}");
            var plan = await FileActionsTestHarness.ReadPlanAsync(dryRun);

            var confirm = await FileActionsTestHarness.ConfirmAsync(client, plan.PlanToken);
            var outcome = await FileActionsTestHarness.ReadOutcomeAsync(confirm);
            if (outcome != "done")
                throw new InvalidOperationException($"AC4 arrange: confirm outcome was '{outcome}' ({confirm.StatusCode})");

            AudioMd5AfterRetag = FileActionTone.AudioMd5(path);
            EnrichmentAfterRetag = await FileActionDbFixtures.ReadEnrichmentFingerprintAsync(database.LibraryConnectionString, mediaId);
            using var retagged = TagLib.File.Create(path);
            FileArtistTagAfterRetag = retagged.Tag.JoinedPerformers ?? "";
        }

        // ── AC5 / AC6 — move within the root, and its own audit row ──────────────────────────
        {
            var srcDir = Directory.CreateDirectory(Path.Combine(root, "ac5-src")).FullName;
            var destDir = Directory.CreateDirectory(Path.Combine(root, "ac5-dest")).FullName;
            var path = FileActionTone.Create(srcDir, "x.mp3");
            var mediaId = await FileActionDbFixtures.InsertMediaRowAsync(
                database.LibraryConnectionString, path, "Mover", "Move Me", null, null, null);

            var dryRun = await FileActionsTestHarness.DryRunAsync(client, mediaId, "move", destDir);
            if (dryRun.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"AC5 arrange: dry-run returned {dryRun.StatusCode}");
            var plan = await FileActionsTestHarness.ReadPlanAsync(dryRun);

            var confirm = await FileActionsTestHarness.ConfirmAsync(client, plan.PlanToken);
            var outcome = await FileActionsTestHarness.ReadOutcomeAsync(confirm);
            if (outcome != "done")
                throw new InvalidOperationException($"AC5 arrange: confirm outcome was '{outcome}' ({confirm.StatusCode})");

            MoveTargetPath = Path.Combine(destDir, "x.mp3");
            MoveFileExistsAtTarget = File.Exists(MoveTargetPath);
            MoveSubjectPath = path;
            MoveConfirmToken = plan.PlanToken;
            var row = await FileActionDbFixtures.ReadMediaRowAsync(database.LibraryConnectionString, mediaId);
            MoveRowPathAfterConfirm = row.Path;
            MoveAuditRow = await FileActionDbFixtures.ReadLatestAuditAsync(database.LibraryConnectionString, mediaId);
        }

        // ── AC7 — TOCTOU: the row is PATCHed between dry-run and confirm ────────────────────
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "ac7")).FullName;
            var path = FileActionTone.Create(dir, "x.mp3");
            var mediaId = await FileActionDbFixtures.InsertMediaRowAsync(
                database.LibraryConnectionString, path, "Toctou", "Toctou", null, null, null);

            var dryRun = await FileActionsTestHarness.DryRunAsync(client, mediaId, "rename");
            if (dryRun.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"AC7 arrange: dry-run returned {dryRun.StatusCode}");
            var plan = await FileActionsTestHarness.ReadPlanAsync(dryRun);

            await FileActionDbFixtures.TouchTitleAsync(database.LibraryConnectionString, mediaId, "changed-since-plan");

            var confirm = await FileActionsTestHarness.ConfirmAsync(client, plan.PlanToken);
            ToctouConfirmStatus = confirm.StatusCode;
            ToctouFileStillAtOriginalPath = File.Exists(path);
        }

        // ── AC9 — a traversal target is refused before any I/O, never echoing the path ──────
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "ac9")).FullName;
            var path = FileActionTone.Create(dir, "x.mp3");
            var mediaId = await FileActionDbFixtures.InsertMediaRowAsync(
                database.LibraryConnectionString, path, "Traverse", "Traverse", null, null, null);

            var response = await FileActionsTestHarness.DryRunAsync(client, mediaId, "rename", "../../etc/x.mp3");
            TraversalStatus = response.StatusCode;
            TraversalBody = await response.Content.ReadAsStringAsync();
        }

        // ── AC10 — a symlinked directory under the root escapes it ──────────────────────────
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "ac10")).FullName;
            var path = FileActionTone.Create(dir, "x.mp3");
            var mediaId = await FileActionDbFixtures.InsertMediaRowAsync(
                database.LibraryConnectionString, path, "Symlink", "Symlink", null, null, null);

            var linkDir = Path.Combine(root, "ac10-link");
            Directory.CreateSymbolicLink(linkDir, outsideRoot);

            var response = await FileActionsTestHarness.DryRunAsync(client, mediaId, "move", linkDir);
            SymlinkEscapeStatus = response.StatusCode;
            SymlinkEscapeSubjectUntouched = File.Exists(path);
        }

        // ── AC11 — a target under a different root, or under an exempt root ─────────────────
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "ac11")).FullName;
            var path = FileActionTone.Create(dir, "x.mp3");
            var mediaId = await FileActionDbFixtures.InsertMediaRowAsync(
                database.LibraryConnectionString, path, "Root", "Root", null, null, null);

            var outsideResponse = await FileActionsTestHarness.DryRunAsync(client, mediaId, "move", anotherRootDir);
            OutsideRootStatus = outsideResponse.StatusCode;

            var exemptResponse = await FileActionsTestHarness.DryRunAsync(client, mediaId, "move", exemptRoot);
            ExemptRootStatus = exemptResponse.StatusCode;
        }

        // ── AC12 — never overwrite: a target appears between dry-run and confirm ─────────────
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "ac12")).FullName;
            var path = FileActionTone.Create(dir, "x.mp3");
            var mediaId = await FileActionDbFixtures.InsertMediaRowAsync(
                database.LibraryConnectionString, path, "Race Artist", "Race Title", null, null, null);

            var dryRun = await FileActionsTestHarness.DryRunAsync(client, mediaId, "rename");
            if (dryRun.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"AC12 arrange: dry-run returned {dryRun.StatusCode}");
            var plan = await FileActionsTestHarness.ReadPlanAsync(dryRun);

            var collideBytes = "COLLIDE"u8.ToArray();
            await File.WriteAllBytesAsync(plan.To, collideBytes);

            var confirm = await FileActionsTestHarness.ConfirmAsync(client, plan.PlanToken);
            NeverOverwriteConfirmStatus = confirm.StatusCode;
            NeverOverwriteBothFilesUnchanged =
                File.Exists(path) && File.ReadAllBytes(plan.To).SequenceEqual(collideBytes);
        }

        // ── AC13 — a failed DB update reverts the move ───────────────────────────────────────
        {
            const string triggerName = "t381_media_update";
            var dir = Directory.CreateDirectory(Path.Combine(root, "ac13")).FullName;
            var path = FileActionTone.Create(dir, "x.mp3");
            var mediaId = await FileActionDbFixtures.InsertMediaRowAsync(
                database.LibraryConnectionString, path, "Revert Artist", "Revert Title", null, null, null);

            var dryRun = await FileActionsTestHarness.DryRunAsync(client, mediaId, "rename");
            if (dryRun.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"AC13 arrange: dry-run returned {dryRun.StatusCode}");
            var plan = await FileActionsTestHarness.ReadPlanAsync(dryRun);

            await FileActionDbFixtures.ArmFailingTriggerAsync(database.LibraryConnectionString, triggerName, mediaId);
            try
            {
                var confirm = await FileActionsTestHarness.ConfirmAsync(client, plan.PlanToken);
                var outcome = await FileActionsTestHarness.ReadOutcomeAsync(confirm);
                if (outcome != "reverted")
                    throw new InvalidOperationException($"AC13 arrange: confirm outcome was '{outcome}' ({confirm.StatusCode})");
            }
            finally
            {
                await FileActionDbFixtures.DisarmTriggerAsync(database.LibraryConnectionString, triggerName);
            }

            RevertFileBackAtOriginalPath = File.Exists(path);
            RevertAuditOutcome = (await FileActionDbFixtures.ReadLatestAuditAsync(database.LibraryConnectionString, mediaId)).Outcome;
        }

        // ── AC14 — an expired token: the shared clock is advanced only here. Every N7 step below
        // still runs on the now-advanced clock, harmlessly — none of them depend on wall-clock
        // timing, only on a token freshly minted and used immediately.
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "ac14")).FullName;
            var path = FileActionTone.Create(dir, "x.mp3");
            var mediaId = await FileActionDbFixtures.InsertMediaRowAsync(
                database.LibraryConnectionString, path, "Expired", "Expired", null, null, null);

            var dryRun = await FileActionsTestHarness.DryRunAsync(client, mediaId, "rename");
            if (dryRun.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"AC14 arrange: dry-run returned {dryRun.StatusCode}");
            var plan = await FileActionsTestHarness.ReadPlanAsync(dryRun);

            clock.Advance(TimeSpan.FromMinutes(11));

            var confirm = await FileActionsTestHarness.ConfirmAsync(client, plan.PlanToken);
            ExpiredTokenConfirmStatus = confirm.StatusCode;
        }

        // ── N7 — dry-run TargetExists: the computed rename target already exists BEFORE dry-run
        // ever runs (the planner's own refusal, distinct from AC12's confirm-time TOCTOU race) ────
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "n7-target-exists")).FullName;
            var path = FileActionTone.Create(dir, "x.mp3");
            var mediaId = await FileActionDbFixtures.InsertMediaRowAsync(
                database.LibraryConnectionString, path, "Occupied Artist", "Occupied Title", null, null, null);

            // The template's own computed name, planted BEFORE dry-run is ever called.
            await File.WriteAllBytesAsync(Path.Combine(dir, "Occupied Artist - Occupied Title.mp3"), "already-here"u8.ToArray());

            var response = await FileActionsTestHarness.DryRunAsync(client, mediaId, "rename");
            DryRunTargetExistsStatus = response.StatusCode;
        }

        // ── N7 — a tampered token (one flipped character) is refused exactly like an invalid one ──
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "n7-tampered")).FullName;
            var path = FileActionTone.Create(dir, "x.mp3");
            var mediaId = await FileActionDbFixtures.InsertMediaRowAsync(
                database.LibraryConnectionString, path, "Tamper", "Tamper", null, null, null);

            var dryRun = await FileActionsTestHarness.DryRunAsync(client, mediaId, "rename");
            if (dryRun.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"N7 tampered-token arrange: dry-run returned {dryRun.StatusCode}");
            var plan = await FileActionsTestHarness.ReadPlanAsync(dryRun);

            var tampered = FlipOneChar(plan.PlanToken);
            var confirm = await FileActionsTestHarness.ConfirmAsync(client, tampered);
            TamperedTokenConfirmStatus = confirm.StatusCode;
        }

        // ── N7 — anonymous is denied on BOTH routes (no login cookie at all, a fresh client off
        // this SAME factory) ─────────────────────────────────────────────────────────────────────
        {
            var anonymousClient = factory.CreateClient();

            var anonymousDryRun = await FileActionsTestHarness.DryRunAsync(anonymousClient, 1, "rename");
            AnonymousDryRunStatus = anonymousDryRun.StatusCode;

            var anonymousConfirm = await FileActionsTestHarness.ConfirmAsync(anonymousClient, "irrelevant-token");
            AnonymousConfirmStatus = anonymousConfirm.StatusCode;
        }

        // ── N7 — an unrecognised verb is a plain 400, never echoing the caller's own value ──────
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "n7-unknown-verb")).FullName;
            var path = FileActionTone.Create(dir, "x.mp3");
            var mediaId = await FileActionDbFixtures.InsertMediaRowAsync(
                database.LibraryConnectionString, path, "Verb", "Verb", null, null, null);

            var response = await FileActionsTestHarness.DryRunAsync(client, mediaId, "obliterate");
            UnknownVerbStatus = response.StatusCode;
            UnknownVerbBody = await response.Content.ReadAsStringAsync();
        }

        // ── N7 — an unknown media id is a plain 404, never echoing the id ───────────────────────
        {
            const long unknownMediaId = 987_654_321;
            var response = await FileActionsTestHarness.DryRunAsync(client, unknownMediaId, "rename");
            UnknownMediaIdStatus = response.StatusCode;
            UnknownMediaIdBody = await response.Content.ReadAsStringAsync();
        }

        // ── N7 — Busy: the shared IScanGate is held OUTSIDE the executor (a synchronous TryEnter
        // straight off this SAME factory's own container) while confirm runs — the executor's own
        // EnterAsync then times out for real, against GateTimeoutSeconds=1 (set on this factory
        // below), never a faked outcome ─────────────────────────────────────────────────────────
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "n7-busy")).FullName;
            var path = FileActionTone.Create(dir, "x.mp3");
            var mediaId = await FileActionDbFixtures.InsertMediaRowAsync(
                database.LibraryConnectionString, path, "Busy", "Busy", null, null, null);

            var dryRun = await FileActionsTestHarness.DryRunAsync(client, mediaId, "rename");
            if (dryRun.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"N7 busy arrange: dry-run returned {dryRun.StatusCode}");
            var plan = await FileActionsTestHarness.ReadPlanAsync(dryRun);

            var gate = factory.Services.GetRequiredService<IScanGate>();
            if (!gate.TryEnter(out var lease))
                throw new InvalidOperationException("N7 busy arrange: could not acquire the scan gate — was it already held?");

            try
            {
                var confirm = await FileActionsTestHarness.ConfirmAsync(client, plan.PlanToken);
                BusyConfirmStatus = confirm.StatusCode;
                BusyRetryAfter = confirm.Headers.RetryAfter?.Delta;
            }
            finally
            {
                lease.Dispose();
            }
        }

        // ── N7 — LeftoverBackup: a pre-existing *.gwbak sibling refuses a retag confirm, and the
        // message names it ───────────────────────────────────────────────────────────────────────
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "n7-leftover-backup")).FullName;
            var path = FileActionTone.Create(dir, "x.mp3", artist: "File Backup Artist");
            var mediaId = await FileActionDbFixtures.InsertMediaRowAsync(
                database.LibraryConnectionString, path, "Catalog Backup Artist", "Backup Title", null, null, null);

            var dryRun = await FileActionsTestHarness.DryRunAsync(client, mediaId, "retag");
            if (dryRun.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"N7 leftover-backup arrange: dry-run returned {dryRun.StatusCode}");
            var plan = await FileActionsTestHarness.ReadPlanAsync(dryRun);

            // <name>.<8hex>.gwbak — the executor's own leftover-backup naming shape.
            var backupSuffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            await File.WriteAllBytesAsync(Path.Combine(dir, $"x.mp3.{backupSuffix}.gwbak"), "leftover"u8.ToArray());

            var confirm = await FileActionsTestHarness.ConfirmAsync(client, plan.PlanToken);
            LeftoverBackupConfirmStatus = confirm.StatusCode;
            var body = JsonDocument.Parse(await confirm.Content.ReadAsStringAsync()).RootElement;
            LeftoverBackupMessage = body.TryGetProperty("message", out var message) ? message.GetString() ?? "" : "";
        }
    }

    static string FlipOneChar(string token)
    {
        // Any alphanumeric position works — base64url's own alphabet has no structural characters
        // (no '.', no '='), so flipping ANY single character invalidates the DataProtection
        // authentication tag without ever touching the token's own length or shape.
        var chars = token.ToCharArray();
        var middle = chars.Length / 2;
        chars[middle] = chars[middle] == 'A' ? 'B' : 'A';
        return new string(chars);
    }

    public Task DisposeAsync()
    {
        foreach (var dir in tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// AC3, isolated: a real, ticking <c>ScanService</c> — see <see cref="ScanZeroDriftWebFactory"/>'s
/// own remarks for why this needs its own database/media root rather than sharing
/// <see cref="FileActionsArc"/>'s.
/// </summary>
public sealed class ScanZeroDriftArc : IAsyncLifetime
{
    public bool FileExistsAtNewPath { get; private set; }
    public bool RowMatchesFileAfterConfirm { get; private set; }

    /// <summary>B2: true only when BOTH hold — (a) a sentinel file dropped AFTER confirm was
    /// genuinely discovered by a real, LATER scan tick (<c>library.media</c>'s own row count grew
    /// by exactly 1, polled via a real predicate, never a blind sleep) — the direct proof a tick
    /// actually ran after the rename, not merely that nothing crashed — and (b) THAT SAME tick left
    /// the renamed row's own state/path/mtime exactly as the confirm wrote them: unchanged, not
    /// re-discovered, not flagged changed or missing. Either half failing means "zero drift" was
    /// never actually demonstrated.</summary>
    public bool ScanRanAndRenamedRowUnchanged { get; private set; }

    string root = "";

    public async Task InitializeAsync()
    {
        await using var database = await ScanZeroDriftDatabase.StartAsync();
        root = FileActionTone.NewTempDir("t381-scan-drift");

        var dir = Directory.CreateDirectory(Path.Combine(root, "a")).FullName;
        var path = FileActionTone.Create(dir, "x.mp3", artist: "Scan Artist", title: "Scan Title");
        var mediaId = await FileActionDbFixtures.InsertMediaRowAsync(
            database.LibraryConnectionString, path, "Scan Artist", "Scan Title", null, null, null);

        await using var factory = new ScanZeroDriftWebFactory(database, root);
        // Touching Services triggers ConfigureWebHost + the real host start (Story297's own
        // precedent) — ScanService's real PeriodicTimer starts running here.
        _ = factory.Services;

        // B2: fail LOUD, at arrange time, if the by-name hosted-service filter ever stops matching
        // ScanService (a rename, or a registration-shape change) — every assertion below is only
        // meaningful if a REAL ScanService is actually the thing ticking.
        if (factory.ScanDescriptorCount < 1)
        {
            throw new InvalidOperationException(
                "AC3 arrange: the by-name IHostedService filter matched no ScanService descriptor " +
                "— its registration shape may have changed.");
        }

        var client = await FileActionsTestHarness.LoggedInClientAsync(factory, ScanZeroDriftWebFactory.Password);

        var dryRun = await FileActionsTestHarness.DryRunAsync(client, mediaId, "rename");
        if (dryRun.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"AC3 arrange: dry-run returned {dryRun.StatusCode}");
        var plan = await FileActionsTestHarness.ReadPlanAsync(dryRun);

        var confirm = await FileActionsTestHarness.ConfirmAsync(client, plan.PlanToken);
        var outcome = await FileActionsTestHarness.ReadOutcomeAsync(confirm);
        if (outcome != "done")
            throw new InvalidOperationException($"AC3 arrange: confirm outcome was '{outcome}' ({confirm.StatusCode})");

        FileExistsAtNewPath = File.Exists(plan.To);

        var row = await FileActionDbFixtures.ReadMediaRowAsync(database.LibraryConnectionString, mediaId);
        var fileInfo = new FileInfo(plan.To);
        var mtimeDriftSeconds = Math.Abs(row.Mtime.ToUnixTimeSeconds() - new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero).ToUnixTimeSeconds());
        RowMatchesFileAfterConfirm = row.Path == plan.To && row.SizeBytes == fileInfo.Length && mtimeDriftSeconds <= 1;

        // B2: a real predicate-poll, not a blind sleep — drop a SENTINEL file (never inserted into
        // the DB by this arrange) and wait for a real, LATER scan tick to discover it (the table's
        // own row count is the one repository-visible effect this suite can poll without reaching
        // into ScanService's own internals). The count growing by exactly 1 is direct, positive
        // proof a tick landed AFTER the rename — not an inference from elapsed wall-clock time.
        var countBeforeSentinel = await FileActionDbFixtures.CountMediaRowsAsync(database.LibraryConnectionString);
        FileActionTone.Create(dir, "sentinel.mp3", artist: "Sentinel", title: "Sentinel");

        await FileActionsTestHarness.WaitUntilAsync(
            async () => await FileActionDbFixtures.CountMediaRowsAsync(database.LibraryConnectionString) == countBeforeSentinel + 1,
            TimeSpan.FromSeconds(30),
            "AC3 arrange: the sentinel file was never discovered by a real scan tick within 30s.");

        // THAT SAME tick (the one that just discovered the sentinel) is what judges the renamed
        // row — re-read it now, immediately after the sentinel's own discovery is confirmed.
        var rowAfterAnotherTick = await FileActionDbFixtures.ReadMediaRowAsync(database.LibraryConnectionString, mediaId);
        var stillUnchanged =
            rowAfterAnotherTick.State == "ready"
            && rowAfterAnotherTick.Path == plan.To
            && rowAfterAnotherTick.SizeBytes == row.SizeBytes
            && Math.Abs(rowAfterAnotherTick.Mtime.ToUnixTimeSeconds() - row.Mtime.ToUnixTimeSeconds()) <= 1;

        ScanRanAndRenamedRowUnchanged = stillUnchanged;
    }

    public Task DisposeAsync()
    {
        try { Directory.Delete(root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
        return Task.CompletedTask;
    }
}
