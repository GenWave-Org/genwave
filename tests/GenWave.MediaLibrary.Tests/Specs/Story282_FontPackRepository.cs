// STORY-282 — FontPackRepository's own SQL, proven against real Postgres (SPEC F104, PLAN T198)
//
// BDD specification — xUnit, Postgres-backed (Category=Integration) via DatabaseCollection. Mirrors
// Story271_ThemeRepository.cs's own precedent (T181/T182): IFontPackStore ships dark (no consumer
// yet), so this file is the ONLY real-SQL proof this seam has — there is no Host.Tests fake to fall
// back on the way ThemeCatalog's own shipped-union-owner load path has (that split needs a
// downstream consumer to drive; nothing downstream of IFontPackStore exists yet).
//
// The golden-font.woff2 fixture (tests/GenWave.Host.Tests/Fixtures, PLAN T193, STORY-279 AC3) is
// read from ITS OWN committed source location rather than duplicated into this project's Fixtures
// directory — one committed binary, reused across both test projects, the same "single source of
// truth" discipline that fixture's own remarks establish for cross-repo parity fixtures.
//
// One assertion per Fact where the scenario allows it; happy path first and exhaustive; the sad
// path (an unknown file) is its own block.

using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureFontPackRepository
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static FontPackRepository Repo(DatabaseFixture db) => new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

    const string Definition = """{"slug":"space-grotesk","family":"Space Grotesk","licence":"OFL-1.1"}""";

    /// <summary>Reads the committed golden woff2 fixture from ITS OWN project's source location —
    /// see this file's own header remarks for why no second copy lives here.</summary>
    static byte[] ReadGoldenWoff2Bytes(DatabaseFixture db) =>
        File.ReadAllBytes(Path.Combine(db.RepoRoot, "tests", "GenWave.Host.Tests", "Fixtures", "golden-font.woff2"));

    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>Structural JSON comparison — see Story271_ThemeRepository.cs's own JsonEquivalent
    /// remarks: jsonb reformats whitespace and reorders keys on write, so a literal string compare of
    /// what was written against what round-trips back is never a fair test.</summary>
    static bool JsonEquivalent(string expected, string actual) =>
        JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual));

    // ---------------------------------------------------------------------
    // HAPPY PATH — UpsertAsync (multi-face) then GetAllAsync/GetFaceByFileAsync round-trip
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioUpsertingANewPackWithFaces(DatabaseFixture db)
    {
        [Fact]
        public async Task GetAllReturnsThePackWithBothFacesAndTheDefinitionRoundTrips()
        {
            await db.ResetFontPackAsync();
            var repo = Repo(db);
            var uprightBytes = ReadGoldenWoff2Bytes(db);
            var italicBytes = "a small deterministic italic face payload, distinct from the golden upright bytes"u8.ToArray();

            await repo.UpsertAsync(
                "space-grotesk", "Space Grotesk", Definition, "space-grotesk-catalog-entry",
                [
                    new FontPackFaceInput("space-grotesk-latin.woff2", uprightBytes, Sha256Hex(uprightBytes)),
                    new FontPackFaceInput("space-grotesk-italic-latin.woff2", italicBytes, Sha256Hex(italicBytes), "italic"),
                ],
                CancellationToken.None);

            var pack = Assert.Single(await repo.GetAllAsync(CancellationToken.None));
            Assert.Equal(
                (Slug: "space-grotesk", Family: "Space Grotesk", DefinitionMatches: true, ImportedFrom: "space-grotesk-catalog-entry", FaceCount: 2),
                (pack.Slug, pack.Family, DefinitionMatches: JsonEquivalent(Definition, pack.Definition), pack.ImportedFrom, FaceCount: pack.Faces.Count));
        }

        [Fact]
        public async Task GetFaceByFileReturnsTheGoldenBytesExactlyWithAMatchingSha256()
        {
            await db.ResetFontPackAsync();
            var repo = Repo(db);
            var uprightBytes = ReadGoldenWoff2Bytes(db);

            await repo.UpsertAsync(
                "space-grotesk", "Space Grotesk", Definition, "space-grotesk-catalog-entry",
                [new FontPackFaceInput("space-grotesk-latin.woff2", uprightBytes, Sha256Hex(uprightBytes))],
                CancellationToken.None);

            var content = await repo.GetFaceByFileAsync("space-grotesk-latin.woff2", CancellationToken.None)
                ?? throw new InvalidOperationException("test arrange: face not found immediately after upsert");

            Assert.Equal(
                (BytesMatch: true, Sha256: Sha256Hex(uprightBytes)),
                (BytesMatch: content.Bytes.SequenceEqual(uprightBytes), content.Sha256));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — re-upsert replaces the pack row AND its ENTIRE face set (SPEC F104)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioReUpsertingAnExistingSlug(DatabaseFixture db)
    {
        [Fact]
        public async Task TheOldFaceIsGoneAndOnlyTheNewOneServes()
        {
            await db.ResetFontPackAsync();
            var repo = Repo(db);
            var originalBytes = "the first-installed upright face payload"u8.ToArray();
            await repo.UpsertAsync(
                "space-grotesk", "Space Grotesk", Definition, "space-grotesk-catalog-entry",
                [new FontPackFaceInput("space-grotesk-latin.woff2", originalBytes, Sha256Hex(originalBytes))],
                CancellationToken.None);

            var replacementBytes = "a re-installed replacement upright face payload"u8.ToArray();
            await repo.UpsertAsync(
                "space-grotesk", "Space Grotesk", Definition, "space-grotesk-catalog-entry",
                [new FontPackFaceInput("space-grotesk-latin-v2.woff2", replacementBytes, Sha256Hex(replacementBytes))],
                CancellationToken.None);

            var newFace = await repo.GetFaceByFileAsync("space-grotesk-latin-v2.woff2", CancellationToken.None);
            Assert.Equal(
                (OldFaceGone: true, NewFaceServesReplacementBytes: true),
                (OldFaceGone: await repo.GetFaceByFileAsync("space-grotesk-latin.woff2", CancellationToken.None) is null,
                 NewFaceServesReplacementBytes: newFace is not null && newFace.Bytes.SequenceEqual(replacementBytes)));
        }

        [Fact]
        public async Task NoSecondPackRowIsCreated()
        {
            await db.ResetFontPackAsync();
            var repo = Repo(db);
            var bytes = "an upright face payload"u8.ToArray();
            await repo.UpsertAsync(
                "space-grotesk", "Space Grotesk", Definition, "space-grotesk-catalog-entry",
                [new FontPackFaceInput("space-grotesk-latin.woff2", bytes, Sha256Hex(bytes))],
                CancellationToken.None);

            await repo.UpsertAsync(
                "space-grotesk", "Space Grotesk", Definition, "file",
                [new FontPackFaceInput("space-grotesk-latin.woff2", bytes, Sha256Hex(bytes))],
                CancellationToken.None);

            // Straight from Postgres, not just the returned row — proves the UNIQUE(slug) ON CONFLICT
            // target updated in place rather than the application racing its own duplicate insert.
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            var count = await conn.ExecuteScalarAsync<int>("select count(*)::int from station.font_pack");
            Assert.Equal(1, count);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — an unknown file is a clean miss, never an exception
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioLookingUpAMissingFile(DatabaseFixture db)
    {
        [Fact]
        public async Task GetFaceByFileReturnsNull()
        {
            await db.ResetFontPackAsync();
            var repo = Repo(db);

            Assert.Null(await repo.GetFaceByFileAsync("no-such-file.woff2", CancellationToken.None));
        }
    }
}
