// STORY-406 — A sponsor is a row, not a string (SPEC F171.1 · PLAN T431)
//
// All three scenario groups share one ephemeral Postgres (the SponsorSchemaCollection fixture)
// started from the sponsor-era db/06 fresh-init, so the whole set costs one docker-compose up/down.
// Every query — including the intentional uniqueness-violation INSERT — runs inside InitializeAsync
// while the database is alive; Facts only read captured Arc properties and never need a live
// DB connection of their own.
//
// This file pins the END-state shape only (a fresh-init db/06 box). The UPGRADE path — an
// existing pre-sponsors box running db/46 in place, including the backfill — is proven separately
// by Story414's Story414_UpgradePathArc, which runs the real db/46 script against a rewound,
// seeded database rather than asserting against a fresh-init table.

using Dapper;
using Npgsql;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

// Row shape for the AC1d pg_get_constraintdef query — Dapper maps by property name, not by
// ValueTuple field, so a small record carries the two columns.
file sealed record CheckConstraintRow(string ConName, string ConDef);

// ── Collection + Arc ─────────────────────────────────────────────────────────────────────────────

[CollectionDefinition(Name)]
public sealed class Story406_SponsorSchemaCollection : ICollectionFixture<Story406_SponsorSchemaArc>
{
    public const string Name = "Story406SponsorSchema";
}

/// <summary>
/// Arranges a fresh-init (sponsor-era db/06) Postgres exactly once for all Story406 facts.
/// Captured properties: column list, name_key fold result, constraint existence,
/// namespace-separation row ids, and the SQL state from the intentional duplicate INSERT
/// (sad path). Facts stay synchronous with no live DB access.
/// </summary>
public sealed class Story406_SponsorSchemaArc : IAsyncLifetime
{
    public IReadOnlyList<string> ColumnNames { get; private set; } = [];
    public string NameKeyForAcmeCorp { get; private set; } = "";
    public bool SponsorPackSlugNameKeyConstraintExists { get; private set; }
    public long OwnerSponsorId { get; private set; }
    public long PackSponsorId { get; private set; }
    public string DuplicateSqlState { get; private set; } = "";
    public IReadOnlyDictionary<string, string> CheckConstraintDefinitions { get; private set; }
        = new Dictionary<string, string>();
    public string PausedColumnDefault { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await using var database = await Story406SponsorSchemaDatabase.StartAsync();

        await using var conn = new NpgsqlConnection(database.StationConnectionString);
        await conn.OpenAsync();

        // AC1a — column list from information_schema (ordered by position, schema-stable)
        ColumnNames = (await conn.QueryAsync<string>(
            "SELECT column_name FROM information_schema.columns " +
            "WHERE table_schema = 'station' AND table_name = 'sponsor' " +
            "ORDER BY ordinal_position")).AsList();

        // AC1b — name_key is a STORED generated fold; insert 'Acme Corp', read back name_key.
        // Expected: station.sponsor_fold('Acme Corp') = "acme corp" — collapse-then-trim (round-3
        // finding N1); a single internal space has nothing to collapse, so it passes through unchanged.
        var ownerId = await conn.ExecuteScalarAsync<long>(
            "INSERT INTO station.sponsor (name, pack_slug) VALUES (@n, NULL) RETURNING id",
            new { n = "Acme Corp" });
        NameKeyForAcmeCorp = await conn.ExecuteScalarAsync<string?>(
            "SELECT name_key FROM station.sponsor WHERE id = @id", new { id = ownerId }) ?? "";
        OwnerSponsorId = ownerId;

        // AC1c — unique constraint must exist in pg_constraint by its schema-canonical name
        SponsorPackSlugNameKeyConstraintExists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM pg_constraint " +
            "WHERE conname = 'sponsor_pack_slug_name_key' " +
            "  AND conrelid = 'station.sponsor'::regclass)");

        // AC1d (round-2 finding F8) — every CHECK on station.sponsor, rendered by Postgres itself
        // rather than guessed from the DDL source (Postgres rewrites e.g. `trim(name)` to
        // `TRIM(BOTH FROM name)` and `BETWEEN 1 AND 120` to two ANDed comparisons).
        var checks = await conn.QueryAsync<CheckConstraintRow>(
            "SELECT conname AS ConName, pg_get_constraintdef(oid) AS ConDef FROM pg_constraint " +
            "WHERE conrelid = 'station.sponsor'::regclass AND contype = 'c'");
        CheckConstraintDefinitions = checks.ToDictionary(c => c.ConName, c => c.ConDef);

        PausedColumnDefault = await conn.ExecuteScalarAsync<string?>(
            "SELECT column_default FROM information_schema.columns " +
            "WHERE table_schema = 'station' AND table_name = 'sponsor' AND column_name = 'paused'") ?? "";

        // AC3 — pack-namespace sponsor with the same folded name coexists with the owner sponsor;
        // the constraint is on (pack_slug, name_key), so NULL vs 'test-pack' are distinct namespaces.
        PackSponsorId = await conn.ExecuteScalarAsync<long>(
            "INSERT INTO station.sponsor (name, pack_slug) VALUES (@n, 'test-pack') RETURNING id",
            new { n = "Acme Corp" });

        // AC2 (sad path) — 'ACME  CORP' folds to 'acme corp' = same name_key as the owner row;
        // the insert must be refused (23505 unique_violation). Capture SqlState for the Fact.
        try
        {
            await conn.ExecuteScalarAsync<long>(
                "INSERT INTO station.sponsor (name, pack_slug) VALUES ('ACME  CORP', NULL) RETURNING id");
        }
        catch (PostgresException ex)
        {
            DuplicateSqlState = ex.SqlState ?? "";
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Scenarios ────────────────────────────────────────────────────────────────────────────────────

public static class Story406_SponsorRow
{
    // ── HAPPY PATH ───────────────────────────────────────────────────────────────────────────────

    [Collection(Story406_SponsorSchemaCollection.Name)]
    public sealed class ScenarioTheTableShape
    {
        readonly Story406_SponsorSchemaArc arc;
        public ScenarioTheTableShape(Story406_SponsorSchemaArc arc) => this.arc = arc;

        [Fact]
        public void StationSponsorCarriesEveryF171Column()
        {
            // Every column from SPEC F171.1 must be present. Column order is not pinned — only
            // presence matters; a future migration may interleave additional columns.
            var required = new[]
            {
                "id", "name", "name_key", "pack_slug",
                "tagline", "about", "phone", "address", "website", "tone",
                "paused", "paused_at", "created_at", "updated_at",
            };
            foreach (var col in required)
                Assert.Contains<string>(col, arc.ColumnNames);
        }

        [Fact]
        public void NameKeyIsAStoredFoldOfName()
            // "Acme Corp" → station.sponsor_fold("Acme Corp") = "acme corp";
            // a single internal space is unchanged by the \s+ collapse.
            => Assert.Equal("acme corp", arc.NameKeyForAcmeCorp);

        [Fact]
        public void TheUniqueConstraintIsOnPackSlugAndNameKeyNullsNotDistinct()
            => Assert.True(arc.SponsorPackSlugNameKeyConstraintExists,
                "pg_constraint row 'sponsor_pack_slug_name_key' not found on station.sponsor — " +
                "the UNIQUE NULLS NOT DISTINCT constraint from db/46 step 1 / db/06 mirror is missing");

        // AC1d (round-2 finding F8) — pin the exact rendered CHECK definitions, captured once
        // against real Postgres 16.4 via pg_get_constraintdef. Postgres normalizes the source DDL
        // (e.g. `trim(name)` → `TRIM(BOTH FROM name)`), so these strings are what Postgres itself
        // produces, not what db/06's source text happens to say.
        [Fact]
        public void NameMustBeOneToOneHundredTwentyCharsAfterTrim()
        {
            var def = arc.CheckConstraintDefinitions["sponsor_name_check"];
            Assert.Contains("char_length(TRIM(BOTH FROM name)) >= 1", def);
            Assert.Contains("char_length(TRIM(BOTH FROM name)) <= 120", def);
        }

        [Fact]
        public void TaglineIsCappedAtOneHundredSixtyChars()
            => Assert.Contains("char_length(tagline) <= 160", arc.CheckConstraintDefinitions["sponsor_tagline_check"]);

        [Fact]
        public void AboutIsCappedAtSixHundredChars()
            => Assert.Contains("char_length(about) <= 600", arc.CheckConstraintDefinitions["sponsor_about_check"]);

        [Fact]
        public void PhoneIsCappedAtFortyChars()
            => Assert.Contains("char_length(phone) <= 40", arc.CheckConstraintDefinitions["sponsor_phone_check"]);

        [Fact]
        public void AddressIsCappedAtTwoHundredChars()
            => Assert.Contains("char_length(address) <= 200", arc.CheckConstraintDefinitions["sponsor_address_check"]);

        [Fact]
        public void WebsiteMustLookLikeAUrlAndIsCappedAtTwoHundredChars()
        {
            var def = arc.CheckConstraintDefinitions["sponsor_website_check"];
            Assert.Contains("website ~ '^https?://'::text", def);
            Assert.Contains("char_length(website) <= 200", def);
        }

        [Fact]
        public void ToneIsCappedAtOneHundredTwentyChars()
            => Assert.Contains("char_length(tone) <= 120", arc.CheckConstraintDefinitions["sponsor_tone_check"]);

        [Fact]
        public void PausedDefaultsToFalse()
            => Assert.Equal("false", arc.PausedColumnDefault);
    }

    [Collection(Story406_SponsorSchemaCollection.Name)]
    public sealed class ScenarioPackNamespaceIsSeparateFromOwnerNamespace
    {
        readonly Story406_SponsorSchemaArc arc;
        public ScenarioPackNamespaceIsSeparateFromOwnerNamespace(Story406_SponsorSchemaArc arc) => this.arc = arc;

        [Fact]
        public void AnOwnerSponsorNamedLikeAPackSponsorIsCreated()
            // The pack-namespace INSERT (pack_slug='test-pack', name='Acme Corp') must succeed
            // alongside the existing owner-namespace row (pack_slug=NULL, name='Acme Corp') —
            // the two namespaces are independent by design (SPEC F171.1).
            => Assert.True(arc.PackSponsorId > 0,
                "pack-namespace sponsor INSERT should return a positive identity value");

        [Fact]
        public void BothRowsCoexist()
            // Two distinct id values prove that neither INSERT was silently blocked.
            => Assert.NotEqual(arc.OwnerSponsorId, arc.PackSponsorId);
    }

    // ── SAD PATH ─────────────────────────────────────────────────────────────────────────────────

    [Collection(Story406_SponsorSchemaCollection.Name)]
    public sealed class ScenarioCaseAndWhitespaceCollapseToOneSponsor
    {
        readonly Story406_SponsorSchemaArc arc;
        public ScenarioCaseAndWhitespaceCollapseToOneSponsor(Story406_SponsorSchemaArc arc) => this.arc = arc;

        [Fact]
        public void ASecondCreateDifferingOnlyByCaseAndSpacingIsRefused()
            // 'ACME  CORP' folds to 'acme corp' (same name_key as the existing owner row); the
            // constraint must refuse the insert with a unique-violation (23505), never accept it.
            => Assert.Equal("23505", arc.DuplicateSqlState);
    }
}

// ── Ephemeral DB ─────────────────────────────────────────────────────────────────────────────────

file sealed class Story406SponsorSchemaDatabase : EphemeralStationDatabase
{
    Story406SponsorSchemaDatabase(string project, string composeFile, string lib, string station)
        : base(project, composeFile, lib, station) { }

    public static async Task<Story406SponsorSchemaDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t406");
        var db = new Story406SponsorSchemaDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}
