namespace GenWave.Host.Api;

/// <summary>
/// The boot-time schema-drift check's own result (SPEC F211.5/F211.6, STORY-485, PLAN T593) — the
/// one value both <see cref="SchemaVersionDriftHostedService"/> (the writer, once, at boot) and
/// <see cref="StatusController"/> (the reader, every poll) share, mirroring
/// <see cref="ProcessStartTime"/>/<see cref="PluginStatusAccessor"/>'s own "write once at boot, read
/// many times" seam.
///
/// <para>
/// <c>GET /api/status</c> is polled far more often than <c>station.schema_migration</c> could ever
/// plausibly change (a live <c>migrate.sh</c> run is an operator action, not a request-time event —
/// the same "needs a restart to take effect" posture every other boot-snapshotted seam in this host
/// follows), so the endpoint deliberately reads THIS cached value rather than re-querying
/// <c>ISchemaJournal</c> on every request: one Postgres round trip per boot, not one per poll.
/// </para>
///
/// <para>
/// <see cref="Applied"/> is <see langword="null"/> before the boot check has run, and stays
/// <see langword="null"/> forever when the journal read empty, missing (a pre-F211 box, 42P01), or
/// threw outright (e.g. the database was unreachable at boot) — <c>GET /api/status</c> does not
/// distinguish those three outcomes on the wire (SPEC F211.6: markers only, no compatibility
/// regime).
/// </para>
/// </summary>
public sealed class SchemaVersionStatus
{
    readonly TaskCompletionSource<int?> checkedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The highest applied migration number the boot check found, or <see langword="null"/>
    /// — see this type's own remarks for every case that reads as null. Reads the value the FIRST
    /// call to <see cref="Record"/> wrote (that method's own remarks); <see langword="null"/> before
    /// any call has landed.</summary>
    public int? Applied => checkedSource.Task.IsCompletedSuccessfully ? checkedSource.Task.Result : null;

    /// <summary>
    /// Completes exactly once, when <see cref="SchemaVersionDriftHostedService"/> finishes its
    /// single boot-time check (successful or not) — by the time this completes, any WARN the check
    /// decided to log has already been logged. A real completion signal a caller can await (e.g. a
    /// test that needs the boot check to have run before asserting), never a polled flag.
    /// </summary>
    public Task Checked => checkedSource.Task;

    /// <summary>
    /// Records the boot check's own result — write-once: the FIRST call wins
    /// (<see cref="TaskCompletionSource{TResult}.TrySetResult"/> both stores the value and completes
    /// <see cref="Checked"/> atomically, so a reader of <see cref="Applied"/> can never observe
    /// <see cref="Checked"/> complete before the value it would read is visible), and every later
    /// call is silently ignored. <see cref="SchemaVersionDriftHostedService"/> calls this exactly
    /// once per boot regardless of which branch it took, so "later calls ignored" is a safety net
    /// this type never expects to actually exercise, not a real retry path.
    /// </summary>
    public void Record(int? applied) => checkedSource.TrySetResult(applied);
}
