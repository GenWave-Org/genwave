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
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureFontPackRepository
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static FontPackRepository Repo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource), NullLogger<FontPackRepository>.Instance);

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

    // ---------------------------------------------------------------------
    // SAD PATH — a mid-upsert failure rolls back EVERYTHING ("abort-pinning", T198/T199 review
    // obligation; the 23505-to-FileCollision mapping itself is gh-#406 slice 2): station.font_pack_face.file
    // is UNIQUE across every installed pack, not scoped per-pack, so installing a brand-new pack whose
    // own face names an already-installed filename raises a REAL Postgres 23505 partway through
    // UpsertAsync's single transaction — AFTER the new pack row itself has already been inserted.
    // UpsertAsync's own catch maps that into a FontPackUpsertResult.FileCollision RETURN value rather
    // than letting the exception escape this seam (L2 Postgres confinement — FontPackController no
    // longer references Npgsql at all); this proves BOTH halves of that mapping against a real
    // Postgres unique_violation: the transaction rolls back the WHOLE write (no trace of the new pack
    // survives, not even the row that landed before the failing statement), and the returned case
    // names the actual colliding file and its owning pack. (FontPackController's own
    // FileCollision-to-409 HTTP mapping is proven separately, against a scripted FakeFontPackStore
    // result, in GenWave.Host.Tests/Specs/Story282_FontPackInstall.cs — this is the real-Postgres half
    // of the same review obligation.)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAMidUpsertFailureAborts(DatabaseFixture db)
    {
        [Fact]
        public async Task ACrossPackFilenameCollisionReturnsFileCollisionNamingTheFileAndOwningPack()
        {
            await db.ResetFontPackAsync();
            var repo = Repo(db);

            // Given an already-installed pack owning "shared-latin.woff2",
            var sharedBytes = "an already-installed pack's own face payload"u8.ToArray();
            await repo.UpsertAsync(
                "pack-a", "Pack A", Definition, "pack-a-catalog-entry",
                [new FontPackFaceInput("shared-latin.woff2", sharedBytes, Sha256Hex(sharedBytes))],
                CancellationToken.None);

            // When installing a DIFFERENT, brand-new pack whose own face declares that SAME
            // filename — UpsertAsync's own transaction inserts the new pack row FIRST, then hits the
            // real unique_violation partway through inserting its faces,
            var collidingBytes = "a different pack's own face payload, same filename"u8.ToArray();
            var result = await repo.UpsertAsync(
                "pack-b", "Pack B", Definition, "pack-b-catalog-entry",
                [new FontPackFaceInput("shared-latin.woff2", collidingBytes, Sha256Hex(collidingBytes))],
                CancellationToken.None);

            // Then the write is refused with a FileCollision naming the actual colliding file and its
            // real owning pack ("pack-a") — never a raw PostgresException escaping this seam.
            var collision = Assert.IsType<FontPackUpsertResult.FileCollision>(result);
            Assert.Equal(("shared-latin.woff2", "pack-a"), (collision.File, collision.OwnerSlug));
        }

        [Fact]
        public async Task ACrossPackFilenameCollisionLeavesNoTraceOfTheAbortedPack()
        {
            await db.ResetFontPackAsync();
            var repo = Repo(db);

            // Given the same already-installed "pack-a" collision setup as the Fact above,
            var sharedBytes = "an already-installed pack's own face payload"u8.ToArray();
            await repo.UpsertAsync(
                "pack-a", "Pack A", Definition, "pack-a-catalog-entry",
                [new FontPackFaceInput("shared-latin.woff2", sharedBytes, Sha256Hex(sharedBytes))],
                CancellationToken.None);

            // When installing a DIFFERENT, brand-new pack whose own face declares that SAME filename,
            var collidingBytes = "a different pack's own face payload, same filename"u8.ToArray();
            await repo.UpsertAsync(
                "pack-b", "Pack B", Definition, "pack-b-catalog-entry",
                [new FontPackFaceInput("shared-latin.woff2", collidingBytes, Sha256Hex(collidingBytes))],
                CancellationToken.None);

            // Then the whole transaction rolled back — no "pack-b" row exists at all, not even the
            // pack row UpsertAsync had already inserted before the face insert failed.
            var packs = await repo.GetAllAsync(CancellationToken.None);
            Assert.DoesNotContain(packs, pack => pack.Slug == "pack-b");
        }
    }
}
