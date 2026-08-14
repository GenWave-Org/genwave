namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// The F105.2 adoption baseline (PLAN T211): every pre-existing law violation found when this
/// suite went live, named, dated, and reasoned. The two entries here are the law's own designed
/// exemption (constructing <c>NpgsqlDataSource</c> is composition-root wiring, not querying —
/// ARCHITECTURE.md "Architecture governance"); the five 2026-08-07 debt rows that once sat
/// alongside them were burned down via gh-#406 (2026-08-13) — every one of those types now reaches
/// Postgres through a <c>GenWave.MediaLibrary.Station</c> repository instead of opening a raw
/// <c>NpgsqlConnection</c> itself. A violation whose member is not on this list still fails
/// (STORY-290 AC6) — see <see cref="DependencyLawAssert"/>.
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
    };
}
