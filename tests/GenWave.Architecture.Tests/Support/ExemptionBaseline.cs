namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// The F105.2 adoption baseline (PLAN T211): every pre-existing law violation found when this
/// suite went live, named, dated, and reasoned. Two of the seven L2 entries are the law's own
/// designed exemption (constructing <c>NpgsqlDataSource</c> is composition-root wiring, not
/// querying — ARCHITECTURE.md "Architecture governance"); the other five are pre-existing debt that
/// was not trivial to fix in this diff (moving working, already-tested Host code into
/// MediaLibrary's repository layer is a real refactor, not a using-swap) — tracked as gh-#406.
/// A violation whose member is not on this list still fails (STORY-290 AC6) — see
/// <see cref="DependencyLawAssert"/>.
/// </summary>
internal static class ExemptionBaseline
{
    public static readonly IReadOnlyList<ArchitectureExemption> Entries = new[]
    {
        // ── Designed exemption: composition-root NpgsqlDataSource construction (wiring, not
        //    querying). Scoped to the exact types that only ever call NpgsqlDataSourceBuilder.Build()
        //    — neither one ever opens a connection or issues a query itself.
        new ArchitectureExemption(
            LawId.L2,
            "GenWave.Host.Seeding.PersonaCardMigrationServiceCollectionExtensions",
            "2026-08-07",
            "Host's composition root: builds the Lazy<NpgsqlDataSource> PersonaCardMigrator resolves, " +
            "never queries itself — the one Host site ARCHITECTURE.md names as the exemption."),
        new ArchitectureExemption(
            LawId.L2,
            "GenWave.MediaLibrary.MediaLibraryServiceCollectionExtensions",
            "2026-08-07",
            "MediaLibrary's own module composition root (AddMediaLibrary): builds the library_svc " +
            "NpgsqlDataSource and sets Dapper's static DefaultTypeMap — wiring/global config, never a " +
            "query, same wiring-not-querying exemption as the Host composition root."),

        // ── Pre-existing debt: genuine querying/exception-coupling outside the repository layer,
        //    found at T211 adoption, not trivial to fix in this diff (follow-up filed as gh-#406).
        new ArchitectureExemption(
            LawId.L2,
            "GenWave.Host.Configuration.StationSettingsConfigurationProvider",
            "2026-08-07",
            "IConfigurationProvider.Load() queries station.settings directly via NpgsqlConnection at " +
            "boot, before the DI container (and any MediaLibrary repository) exists to inject. " +
            "Pre-existing (STORY-042); not trivial to fix in this diff — follow-up gh-#406."),
        new ArchitectureExemption(
            LawId.L2,
            "GenWave.Host.Configuration.StationSettingsStore",
            "2026-08-07",
            "Reads/writes station.settings directly via NpgsqlConnection (the write side of the " +
            "settings overlay). Pre-existing (STORY-042); not trivial to fix in this diff — follow-up gh-#406."),
        new ArchitectureExemption(
            LawId.L2,
            "GenWave.Host.Seeding.SafeLoopSeedMarkerStore",
            "2026-08-07",
            "Reads/writes the boot-seed marker directly via NpgsqlConnection on the station.settings " +
            "table (F27.10). Pre-existing; not trivial to fix in this diff — follow-up gh-#406."),
        new ArchitectureExemption(
            LawId.L2,
            "GenWave.Host.Api.FontPackController",
            "2026-08-07",
            "Catches Npgsql.PostgresException directly to translate a unique_violation into a 409 " +
            "(documented house idiom, no Npgsql.PostgresErrorCodes dependency). Pre-existing; not " +
            "trivial to fix in this diff — follow-up gh-#406."),
        new ArchitectureExemption(
            LawId.L2,
            "GenWave.Host.Api.ScheduleController",
            "2026-08-07",
            "Catches Npgsql.PostgresException directly to detect the persona-deleted-mid-write race " +
            "(gh-#255, documented house idiom). Pre-existing; not trivial to fix in this diff — " +
            "follow-up gh-#406."),
    };
}
