using GenWave.Core;
using GenWave.Core.Abstractions;

namespace GenWave.Host.Api;

/// <summary>
/// The one boot-time schema-drift check (SPEC F211.6, STORY-485, PLAN T593, gh-#9/gh-#868) — reads
/// <see cref="ISchemaJournal.GetAppliedAsync"/> exactly once per boot, logs exactly one WARN naming
/// both numbers (<c>"none"</c> for a null applied) plus <c>./migrate.sh</c> when <c>applied &lt; </c>
/// <see cref="SchemaVersion.Expected"/> or applied is null, and only THEN records the result on
/// <see cref="SchemaVersionStatus"/> — a waiter on <see cref="SchemaVersionStatus.Checked"/> can never
/// observe completion before the WARN (if any) has already been logged. Matched or ahead: silent
/// (SPEC F211.6 — drift is reported, never enforced; a real compatibility regime is gh-#12's job, not
/// this one's).
///
/// <para>
/// <b>Never blocks boot, never throws.</b> A <see cref="BackgroundService"/>, not an inline
/// <c>await</c> in <c>Program.cs</c> — <see cref="ISchemaJournal.GetAppliedAsync"/> is a real
/// Postgres round trip (<c>SchemaJournalRepository</c>'s own remarks), and an unreachable database
/// could otherwise stall Kestrel from ever accepting a request. <c>ExecuteAsync</c>'s own
/// <c>StartAsync</c> (the <see cref="BackgroundService"/> base) returns the instant the first
/// incomplete <see langword="await"/> is hit, so host startup never waits on this — mirrors
/// <c>ThemeCatalogOwnerLoadHostedService</c>/<c>PersonaCardMigrationHostedService</c>'s own "runs
/// once per boot, without blocking startup" shape one file over. Any exception the journal throws
/// beyond the 42P01/empty cases <see cref="ISchemaJournal.GetAppliedAsync"/>'s own remarks already
/// degrade to null (a genuine infra fault — the database unreachable at the exact instant this
/// runs) is caught here too: it degrades to the SAME "applied is null" outcome, reported by the SAME
/// WARN, with the exception attached rather than a second, redundant log line or an unhandled
/// exception that would crash this one background task.
/// </para>
/// </summary>
sealed class SchemaVersionDriftHostedService(
    ISchemaJournal journal,
    SchemaVersionStatus status,
    ILogger<SchemaVersionDriftHostedService> logger) : BackgroundService
{
    const string WarnFormat =
        "Schema drift: database has applied {Applied} but this build expects {Expected} — run ./migrate.sh";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int? applied;
        try
        {
            applied = await journal.GetAppliedAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down before the read finished — no WARN (this isn't a drift finding,
            // it's a boot that never got to look); Checked still completes, Applied stays null, so
            // a waiter never hangs and the next boot checks again.
            status.Record(null);
            return;
        }
        catch (Exception ex)
        {
            // Any OTHER failure (database unreachable, etc.) — SchemaJournalRepository's own
            // remarks already degrade the 42P01/empty cases to null; this is the genuine-infra-
            // fault case one level up. Treated identically to an empty journal ("none"), with ex
            // attached to this SAME single WARN rather than a second, redundant log line.
            logger.LogWarning(ex, WarnFormat, "none", SchemaVersion.Expected);
            status.Record(null);
            return;
        }

        if (applied is int a && a >= SchemaVersion.Expected)
        {
            status.Record(applied); // Matched or ahead of the build's own expectation — nothing to report.
            return;
        }

        logger.LogWarning(WarnFormat, applied.HasValue ? (object)applied.Value : "none", SchemaVersion.Expected);
        status.Record(applied);
    }
}
