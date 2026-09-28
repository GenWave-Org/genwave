namespace GenWave.Core.Abstractions;

/// <summary>
/// Reads what the database itself believes it has applied (SPEC F211.4, STORY-484, PLAN T592,
/// gh-#868) — the "Applied" half of the schema-version pin, the <see cref="SchemaVersion.Expected"/>
/// constant being the "Expected" half. Backed by <c>station.schema_migration</c> (SPEC F211.3, PLAN
/// T591), the journal <c>migrate.sh</c> upserts one row into per <c>db/NN-*-migration.sh</c> it runs.
/// </summary>
public interface ISchemaJournal
{
    /// <summary>
    /// The highest <c>NN</c> among journalled <c>script</c> rows, or <see langword="null"/> when the
    /// journal has no rows to read a number from — an empty <c>station.schema_migration</c> table (the
    /// raw <c>docker compose up</c> that skipped <c>migrate.sh</c>, per SPEC F211.4) and a missing
    /// table (42P01 — a pre-F211 box that never ran the new <c>migrate.sh</c> at all) both read as
    /// <see langword="null"/> rather than throwing — the caller distinguishes "nothing applied yet"
    /// from a genuine infrastructure fault by any OTHER means it already has, not by this method's
    /// exception type.
    /// </summary>
    Task<int?> GetAppliedAsync(CancellationToken ct);
}
