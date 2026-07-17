// STORY-032 — Catalog reads project energy columns onto MediaReference
//
// BDD specification — xUnit. Integration via DatabaseCollection.
// Mirrors Story019_CatalogProjectsCueColumns seam-for-seam but for energy.

using Dapper;
using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureCatalogProjectsEnergyColumns
{
    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMediaRowExposesEnergyColumns(DatabaseFixture db)
    {
        [Fact]
        public void MediaRowHasNullableIntroEnergyProperty()
        {
            _ = db;
            var prop = typeof(MediaRow).GetProperty("IntroEnergy");
            Assert.NotNull(prop);
            Assert.Equal(typeof(double?), prop.PropertyType);
        }

        [Fact]
        public void MediaRowHasNullableOutroEnergyProperty()
        {
            _ = db;
            var prop = typeof(MediaRow).GetProperty("OutroEnergy");
            Assert.NotNull(prop);
            Assert.Equal(typeof(double?), prop.PropertyType);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBothColumnsPresentProjectValues(DatabaseFixture db)
    {
        [Fact]
        public async Task IntroEnergyRoundTripsFromTheRow()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await SeedReadyRowWithEnergyAsync(db, introEnergy: 0.82, outroEnergy: 0.31);
            var scope = new LibraryScope([1L]);
            var reference = await ((IMediaCatalog)repo).GetByIdAsync(scope, id.ToString(), CancellationToken.None);

            Assert.NotNull(reference);
            Assert.Equal(0.82, reference.IntroEnergy);
        }

        [Fact]
        public async Task OutroEnergyRoundTripsFromTheRow()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await SeedReadyRowWithEnergyAsync(db, introEnergy: 0.82, outroEnergy: 0.31);
            var scope = new LibraryScope([1L]);
            var reference = await ((IMediaCatalog)repo).GetByIdAsync(scope, id.ToString(), CancellationToken.None);

            Assert.NotNull(reference);
            Assert.Equal(0.31, reference.OutroEnergy);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBothColumnsNull(DatabaseFixture db)
    {
        [Fact]
        public async Task NullColumnsProjectNullIntroEnergy()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            // Both energy columns left null by default via WriteEnrichmentAsync (energy not yet wired).
            var id = await repo.InsertDiscoveredAsync("/media/null-energy.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var reference = await ((IMediaCatalog)repo).GetByIdAsync(scope, id.ToString(), CancellationToken.None);

            Assert.NotNull(reference);
            Assert.Null(reference.IntroEnergy);
        }

        [Fact]
        public async Task NullColumnsProjectNullOutroEnergy()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/media/null-energy-b.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var reference = await ((IMediaCatalog)repo).GetByIdAsync(scope, id.ToString(), CancellationToken.None);

            Assert.NotNull(reference);
            Assert.Null(reference.OutroEnergy);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SAD PATH
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAsymmetricNullIsDefensive(DatabaseFixture db)
    {
        [Fact]
        public async Task OneColumnNullProjectsBothEnergiesNull()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            // intro_energy present, outro_energy null — asymmetric data-integrity edge case.
            var id = await SeedReadyRowWithEnergyAsync(db, introEnergy: 0.5, outroEnergy: null);
            var scope = new LibraryScope([1L]);
            var reference = await ((IMediaCatalog)repo).GetByIdAsync(scope, id.ToString(), CancellationToken.None);

            Assert.NotNull(reference);
            Assert.Null(reference.IntroEnergy);
            Assert.Null(reference.OutroEnergy);
        }

        [Fact]
        public void AsymmetricNullLogsAWarning()
        {
            // Test ResolveEnergy directly via an asymmetric MediaRow; capture the WARN log.
            var capturing = new CapturingLogger();
            var row = new MediaRow { Id = 42, IntroEnergy = 0.7, OutroEnergy = null };

            var (intro, outro) = row.ResolveEnergy(capturing);

            Assert.Null(intro);
            Assert.Null(outro);
            Assert.Contains(capturing.Warnings, w => w.Contains("42") && w.Contains("asymmetric"));
        }

        /// <summary>
        /// Minimal ILogger implementation that collects Warning messages for assertion.
        /// Test-scope only; not part of production code.
        /// </summary>
        private sealed class CapturingLogger : ILogger
        {
            public List<string> Warnings { get; } = [];

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Warning)
                    Warnings.Add(formatter(state, exception));
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Also verify via GetRandomReadyAsync (covers that SELECT path too)
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioGetRandomReadySurfacesEnergyPoints(DatabaseFixture db)
    {
        [Fact]
        public async Task GetRandomReadyReturnsEnergyValues()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            await SeedReadyRowWithEnergyAsync(db, introEnergy: 0.9, outroEnergy: 0.1);
            var scope = new LibraryScope([1L]);
            var reference = await ((IMediaCatalog)repo).GetRandomReadyAsync(scope, [], CancellationToken.None);

            Assert.NotNull(reference);
            Assert.Equal(0.9, reference.IntroEnergy);
            Assert.Equal(0.1, reference.OutroEnergy);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Shared seed helper
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts a ready+measurable row and writes energy values directly via SQL, because
    /// WriteEnrichmentAsync does not yet carry energy fields (that lands in E5). Returns the row id.
    /// </summary>
    static async Task<long> SeedReadyRowWithEnergyAsync(
        DatabaseFixture db, double? introEnergy, double? outroEnergy)
    {
        var repo = Harness.Repo(db);
        var id = await repo.InsertDiscoveredAsync(
            $"/media/energy-{Guid.NewGuid():N}.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

        // Patch energy columns directly — WriteEnrichmentAsync does not carry them yet (E5).
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "update library.media set intro_energy = @intro, outro_energy = @outro where id = @id",
            new { intro = introEnergy, outro = outroEnergy, id });

        return id;
    }
}
