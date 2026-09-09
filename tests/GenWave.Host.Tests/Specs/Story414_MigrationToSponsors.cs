// STORY-414 — The live station migrates cleanly to sponsors (SPEC F172.1/F172.2 · PLAN T431)
//
// Two Arcs, two text-pin Scenarios:
//   • Story414_UpgradePathArc (AC2/AC3/AC4/AC5(b,c)/AC6) — rewinds a fresh-init sponsor-era database (via
//     Support/db46-rewind-to-db45.sh) to db/45-era shape, seeds a realistic pre-migration fixture,
//     then runs the REAL db/46-sponsor-migration.sh via RunFileInContainer — never a hand-rolled
//     stand-in of its DDL. The rewind step exists because EphemeralStationDatabase always starts
//     from db/06's sponsor-era fresh-init; without it, there is no pre-migration state left to
//     migrate FROM. A second db/46 run (still inside this same Arc) proves AC6's idempotency.
//   • Story414_FullMigrateLoopArc (round-2 finding F1's regression pin) — same rewind, then every
//     db/*-migration.sh file, sorted, exactly mirroring migrate.sh's own glob loop. This is what
//     catches an ordering bug like F1: db/06 runs before db/46 in that sorted order (39 other
//     db/*-migration.sh files sort between them — never "immediately"), so any fix that only
//     works when db/46 runs FIRST (out of order) would pass Story406/the UpgradePathArc above
//     and still deadlock a real upgrading box.
//   • ScenarioMigrationRunsWithTheApiDown (AC1) — pure file-read index comparisons on launch.sh's
//     own text (Story343's own pattern): the migrate.sh invocation must appear before the compose up
//     that brings up the api, in BOTH the pinned and dev flows.
//   • ScenarioTheFoldExpressionIsTextPinned (AC5(a)) — pure file-read text pins: the fold
//     function's body appears verbatim in both db/46 (step 0) and db/06 (its fresh-init mirror);
//     the N6 drift-tripwire then counts the raw collapse expression in CODE (never a comment) —
//     exactly once in db/06 (the function body) and exactly twice in db/46 (the function body
//     plus the one display-name normaliser in the backfill) — a future hand-copied THIRD raw
//     collapse expression anywhere flips these counts and goes red.
//
// Deviation from STORY-414 AC2's literal fixture text ("two briefs 'Al's Diner' (pack_slug='p')"):
// the OLD ad_brief_pack_slug_brand_key constraint this rewound database still carries is
// UNIQUE NULLS NOT DISTINCT (pack_slug, brand) — premise plays no part in that key, so two
// LITERALLY-identical (pack_slug, brand) rows cannot coexist pre-migration regardless of premise.
// The fixture's pack='p' PAIR ONLY (never the owner-namespace trio below it, which uses the exact
// literal spellings STORY-414 asks for) uses two DIFFERENTLY-SPELLED brands that fold to the
// identical sponsor key ("Al's Diner" / "AL'S DINER") — this still proves AC2's collapse-to-one-
// sponsor and AC3's earliest-spelling-wins exactly as intended, without asserting a database state
// Postgres itself would refuse to create.
//
// Deviation from AC5's literal text-pin (round-3 finding N1): the fold expression is no longer
// duplicated inline at each call site — it is centralised into ONE IMMUTABLE SQL function,
// station.sponsor_fold(text), defined once (db/46 step 0, mirrored verbatim in db/06) and called
// from both station.sponsor.name_key and station.ad_brief.premise_key. AC5's pin therefore now
// targets three things instead of two inline duplicates: (a) the function's own body text, pinned
// identically in both files; (b) Postgres's own rendering of each generated column's expression
// (via pg_get_expr on pg_attrdef — Postgres normalises source DDL, so this is what Postgres itself
// stores, not a guess from the source text; rendered UNqualified, "sponsor_fold(...)" not "station.
// sponsor_fold(...)" — round-4 AC5 finding: the Arc SETs search_path = station explicitly on its
// own connection immediately before this read, so the fact owns this precondition itself rather
// than leaning on the fixture connection string's own Search Path=station as an implicit side
// effect; ruleutils only schema-qualifies a call when it would NOT resolve unambiguously under the
// deparsing session's own search_path); (c) the function's actual fold behaviour, queried live
// against a mixed-whitespace input. See ScenarioTheFoldFunctionIsCentralizedAndCorrect below.

using Dapper;
using Npgsql;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

// Row shape for the captured post-migration sponsor list — Dapper maps by property name.
file sealed record SponsorRow(long Id, string Name, string? PackSlug);

// Round-4 finding P1 — row shape for a dedupe-pass brief (keeper or disabled duplicate), carrying
// its own sponsor_id so a Fact can assert "same sponsor" directly rather than trusting the WHERE
// clause that fetched it.
file sealed record DedupeBriefRow(long Id, bool Enabled, string Premise, long SponsorId);

// Round-4 finding P4 — shared by both Arcs: proves the rewind script actually landed on a
// genuinely pre-sponsor shape (zero sponsor-named traces anywhere in the station schema, and
// ad_brief.brand present) before either Arc seeds anything or runs db/46 for the first time.
// Commenting out the rewind script's own DROP TABLE IF EXISTS station.sponsor (or any of its
// other DROPs) is exactly what this sentinel exists to catch — without it, a broken rewind would
// only ever surface indirectly, as a downstream db/46 failure whose real cause (the rewind never
// actually happened) is easy to misdiagnose as a db/46 bug instead.
file static class RewindSentinel
{
    public static async Task<(long TraceCount, bool AdBriefBrandExists)> CaptureAsync(NpgsqlConnection conn)
    {
        var traceCount = await conn.ExecuteScalarAsync<long>("""
            SELECT
                (SELECT COUNT(*) FROM information_schema.tables
                 WHERE table_schema = 'station' AND table_name LIKE '%sponsor%') +
                (SELECT COUNT(*) FROM information_schema.columns
                 WHERE table_schema = 'station' AND column_name LIKE '%sponsor%') +
                (SELECT COUNT(*) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                 WHERE n.nspname = 'station' AND p.proname LIKE '%sponsor%') +
                (SELECT COUNT(*) FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace
                 WHERE n.nspname = 'station' AND c.conname LIKE '%sponsor%') +
                (SELECT COUNT(*) FROM pg_indexes
                 WHERE schemaname = 'station' AND indexname LIKE '%sponsor%')
            """);

        var adBriefBrandExists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM information_schema.columns " +
            "WHERE table_schema = 'station' AND table_name = 'ad_brief' AND column_name = 'brand')");

        return (traceCount, adBriefBrandExists);
    }
}

// ── Collections + Arcs ───────────────────────────────────────────────────────────────────────────

[CollectionDefinition(Name)]
public sealed class Story414_UpgradePathCollection : ICollectionFixture<Story414_UpgradePathArc>
{
    public const string Name = "Story414UpgradePath";
}

[CollectionDefinition(Name)]
public sealed class Story414_FullMigrateLoopCollection : ICollectionFixture<Story414_FullMigrateLoopArc>
{
    public const string Name = "Story414FullMigrateLoop";
}

/// <summary>
/// Rewinds a fresh-init sponsor-era Postgres to db/45-era shape, seeds a realistic pre-migration
/// fixture — seventeen ad_brief rows in five groups: owner-namespace Acme rows (two original
/// spellings + one over-length brand + a tab/newline-padded pair + a blank-premise pair = 7),
/// owner-namespace Al's Diner trio (3), pack-namespace 'p' Al's Diner pair (2), pack-namespace 'q'
/// whitespace-only pair (2), and the round-4 dedupe trio (3) — 7 + 3 + 2 + 2 + 3 = 17. Runs the
/// REAL db/46 script (must throw on failure — no swallowed exceptions on this first run),
/// captures the resulting sponsor rows and null-sponsor_id counts (AC2/AC3/AC4), then runs db/46 a
/// second time to prove AC6's idempotency (this second run's own exception, if any, IS captured — a
/// failure here is a Fact assertion, not a fixture-wide abort).
/// </summary>
public sealed class Story414_UpgradePathArc : IAsyncLifetime
{
    public IReadOnlyList<(long Id, string Name, string? PackSlug)> Sponsors { get; private set; } = [];
    public long NullSponsorIdBriefCount { get; private set; }
    public long NullSponsorIdSpotCount { get; private set; }
    public string BriefSponsorIdIsNullable { get; private set; } = "YES";
    public string SpotSponsorIdIsNullable { get; private set; } = "YES";
    public long SponsorCountBeforeSecondRun { get; private set; }
    public long SponsorCountAfterSecondRun { get; private set; }
    public bool SecondRunThrew { get; private set; }
    public long BlankPremiseBriefCountForOwnerSponsor { get; private set; }

    // Round-3 finding N4 — the fixture must exercise N1's fix through the REAL db/46, not just
    // assert against literal duplicate strings.
    public long AcmeOwnerSponsorBriefCount { get; private set; }
    public long TabAndNewlinePaddedBriefCountUnderOwnerSponsor { get; private set; }
    public string? AlsDinerOwnerDoubleSpacedPremiseKey { get; private set; }

    // Round-3 finding AC5 — the centralised fold function, pinned three ways (see this file's own
    // header deviation note): function body text (in ScenarioTheFoldExpressionIsTextPinned, no DB
    // needed), Postgres's own rendering of each generated column (b, below), and live fold
    // behaviour on a mixed-whitespace input (c, below).
    public string SponsorNameKeyExpr { get; private set; } = "";
    public string AdBriefPremiseKeyExpr { get; private set; } = "";
    public string FoldedMixedWhitespaceAlsDiner { get; private set; } = "";

    // Round-4 finding P1 — the angle-collision pass's own outcome: the keeper stays enabled with
    // its premise untouched, the two later owner duplicates are disabled with a note naming the
    // keeper, both (plus the original owner Al's Diner trio) share the one owner sponsor_id, and
    // nothing is ever deleted (STORY-414: "a boring restart, not a data-loss incident").
    public long AlsDinerOwnerSponsorTotalBriefCount { get; private set; }
    public long DedupeKeeperBriefId { get; private set; }
    public bool DedupeKeeperBriefEnabled { get; private set; }
    public string DedupeKeeperBriefPremise { get; private set; } = "";
    public long DedupeKeeperBriefSponsorId { get; private set; }

    // Round-4 finding R1' (supersedes round-3 finding R1's table-wide claim): now OWNER-PASS-ONLY —
    // the pack-identity pass no longer suffixes a "[duplicate #<id> of brief #<keeper>, disabled by
    // db/46]" premise at all (it DELETES its losing row outright instead, see the pack-identity
    // facts below), so this query only ever catches the angle-collision pass's own disabled
    // duplicates: the two owner Al's Diner rows (08:04, 08:05).
    public IReadOnlyList<(long Id, bool Enabled, string Premise, long SponsorId)> DedupeDisabledBriefs
        { get; private set; } = [];
    public long TotalAdBriefCountAfterMigration { get; private set; }

    // Round-4 finding R1' (supersedes round-3 finding R1) — the pack-identity dedupe pass's own
    // outcome, pack='p' pair: two differently-spelled brands fold to ONE sponsor but carry DIFFERENT
    // premises ('Family owned since 1990' vs 'Weekend brunch special'), so the angle-collision pass
    // never touches them — only the pack-identity pass (grouped by (pack_slug, sponsor_id) alone,
    // ignoring premise) does. The keeper (09:00, earliest) stays enabled with its premise untouched;
    // the later row (09:01) is DELETED outright — no disable, no suffix, so it never reaches the
    // table-wide DedupeDisabledBriefs list above. PackAlsDinerDuplicateBriefId is captured from the
    // SEED connection, before db/46 ever runs, so the post-migration facts below can assert this
    // exact row is gone BY ID — not merely "no row with this premise text", which a rename could
    // also satisfy.
    public long PackAlsDinerKeeperBriefId { get; private set; }
    public bool PackAlsDinerKeeperBriefEnabled { get; private set; }
    public string PackAlsDinerKeeperBriefPremise { get; private set; } = "";
    public long PackAlsDinerSponsorTotalBriefCount { get; private set; }
    public long PackAlsDinerDuplicateBriefId { get; private set; }
    public bool PackAlsDinerDuplicateBriefExistsById { get; private set; }
    public bool PackAlsDinerDuplicatePremiseExistsAnywhere { get; private set; }

    // Round-4 finding R1' — ad_spot carries no FK to ad_brief (its own sponsor_id backfill runs
    // independently, over the ad_spot slice of the same sponsor_backfill_norm temp table), so the
    // pack 'p' spot must still resolve to the pack Al's Diner sponsor even though the duplicate
    // BRIEF that shared its (pack_slug, sponsor_id) slot was deleted.
    public long AdSpotPackAlsDinerSponsorId { get; private set; }

    // Round-3 finding R1 — Postgres's own rendering of the new partial unique index (pg_get_indexdef
    // on pg_index, same "trust Postgres's own normalisation, not a guess from source DDL" reasoning
    // AC5(b)'s pg_get_expr reads already apply to the two generated-column expressions below).
    public string AdBriefPackSlugSponsorIdIndexDef { get; private set; } = "";

    // Round-4 finding P4 — the rewind sentinel: this Arc must prove its "upgrade path" really
    // starts from a genuinely pre-sponsor shape immediately after the rewind script runs, before
    // any seed data or db/46 run — never asserted only implicitly by db/46 later succeeding.
    public long RewindSponsorTraceCount { get; private set; }
    public bool RewindAdBriefBrandColumnExists { get; private set; }

    public async Task InitializeAsync()
    {
        await using var database = await Story414SponsorDatabase.StartAsync();
        var repoRoot = RepoRootLocator.Find(AppContext.BaseDirectory);

        // Tear the sponsor-era fresh-init back down to the shape any live pre-migration box
        // actually has (round-2 finding F4) — the ONLY way to exercise the real backfill without
        // hand-rolling a second, drift-prone copy of db/46's own DDL.
        database.RunFileInContainer(
            Path.Combine(repoRoot, "tests", "GenWave.Host.Tests", "Support", "db46-rewind-to-db45.sh"));

        await using (var seedConn = new NpgsqlConnection(database.StationConnectionString))
        {
            await seedConn.OpenAsync();

            // Round-4 finding P4 — assert the rewind really landed on a pre-sponsor shape before
            // seeding anything or running db/46 for the first time.
            (RewindSponsorTraceCount, RewindAdBriefBrandColumnExists) =
                await RewindSentinel.CaptureAsync(seedConn);

            // Owner-namespace Acme pair: two spellings fold to "acme corp"; earliest (2026-01-01)
            // wins as the canonical name (AC3). One over-length brand (150 chars, cap is 120) is
            // NOT a third member of that fold — it truncates to a DIFFERENT 120-char name and lands
            // on its own separate sponsor, proving F3's truncate-not-abort fix rather than aborting
            // the whole backfill.
            //
            // Round-2 finding F2(b): two MORE owner-namespace rows, differently-spelled so as not to
            // collide with the OLD (pre-migration) ad_brief_pack_slug_brand_key literal-duplicate
            // constraint (see this file's header deviation note). Round-3 finding N1/N4: their
            // premises are now E'\t' (a bare tab) and '  ' (two plain spaces) — the exact case that
            // exposed the trim-first bug (a bare tab survives a leading btrim untouched, then
            // collapses into a stray SPACE instead of nothing). Both must still fold premise_key to
            // NULL (nullif-wrapped) and land on the SAME Acme Corp sponsor as the pair above —
            // proving the constraint stays plain UNIQUE, not NULLS NOT DISTINCT: two NULL
            // premise_keys under one sponsor_id must coexist (SPEC F171.6, "a NULL premise is no
            // angle" — a blank premise is not a duplicate angle to collide on).
            //
            // Round-3 finding N1/N4: a tab-PADDED and a newline-PADDED spelling of 'Acme Corp' —
            // the literal bug-triggering inputs from N1's own writeup — must fold to the SAME
            // "acme corp" name_key as the plain spelling and land on the SAME owner sponsor, not a
            // second, stray sponsor named " acme corp" or "acme corp ".
            //
            // Owner-namespace Al's Diner trio (pack_slug NULL): the LITERAL spellings STORY-414 AC2
            // asks for ("Al's Diner" / "al's diner" / "AL'S  DINER" — the third with an internal
            // DOUBLE space), seedable here because this trio sits in the owner namespace, distinct
            // from the differently-spelled pack='p' pair below (see this file's header deviation
            // note — the pack pair alone carries that deviation). The double-spaced premise on the
            // second row ('Weekend  Brunch  Special') exists to pin premise_key's own collapse of an
            // INTERNAL run of whitespace, not just an edge one.
            //
            // Pack-namespace pair under 'p': two DIFFERENTLY-SPELLED brands that fold the same (see
            // this file's own header deviation note for why they cannot be literal duplicates here),
            // carrying two DIFFERENT premises (round-4 finding R1', supersedes round-3 finding R1) —
            // the angle-collision pass never touches this pair (their premise_keys differ), only the
            // pack-identity pass does, grouped by (pack_slug, sponsor_id) alone: the earliest (09:00)
            // stays enabled, the later (09:01) is DELETED outright — unlike an angle-collision
            // duplicate, never disabled, never suffixed — see ScenarioPackBriefsAreCappedAtOnePerSponsor
            // below.
            //
            // Pack-namespace 'q' pair: two WHITESPACE-ONLY brands ('   ' and E'\t ') that must both
            // collapse to the SAME placeholder sponsor named "Unnamed sponsor" — proving the
            // backfill's norm_name placeholder (round-2 finding F3) fires for either plain-space-only
            // or tab-containing whitespace-only input, not just the plain-space case a bare btrim
            // alone would already have handled.
            //
            // Round-4 finding P1: three MORE owner-namespace rows, three brand spellings never used
            // above (so the OLD, pre-migration ad_brief_pack_slug_brand_key literal-(pack_slug,
            // brand) constraint has nothing to collide on — it never looked at premise at all),
            // whose premises ('Open at six' / '  Open  At  Six ' / 'OPEN   AT SIX') all fold to the
            // SAME premise_key. All three land on the SAME owner "Al's Diner" sponsor as the trio
            // above (same pack_slug/name_key group), handing step 9's NEW
            // ad_brief_sponsor_id_premise_key constraint exactly the collision it exists to catch —
            // the dedupe pass below (db/46, between the sponsor_id backfill and step 9) must resolve
            // it by keeping the earliest row (08:03) and disabling the other two, or this whole Arc
            // reds out on step 9's own ADD CONSTRAINT. A THIRD duplicate (not just two) proves the
            // dedupe's rn > 1 threshold catches more than a single pair.
            await seedConn.ExecuteAsync("""
                INSERT INTO station.ad_brief (pack_slug, brand, premise, created_at) VALUES
                  (NULL, 'Acme Corp',        'Family owned since 1990',          '2026-01-01T10:00:00Z'),
                  (NULL, 'ACME CORP',        'Spring sale event',                '2026-01-02T10:00:00Z'),
                  (NULL, repeat('A', 150),   'Overlong brand test',              '2026-01-03T10:00:00Z'),
                  (NULL, 'Acme Corp ',       E'\t',                              '2026-01-01T11:00:00Z'),
                  (NULL, ' ACME CORP',       '  ',                               '2026-01-01T12:00:00Z'),
                  (NULL, E'\tAcme Corp',     'Tab-padded brand test',            '2026-01-01T13:00:00Z'),
                  (NULL, E'Acme Corp\n',     'Newline-padded brand test',        '2026-01-01T14:00:00Z'),
                  (NULL, 'Al''s Diner',      'Neighborhood favorite since 1985', '2026-01-01T08:00:00Z'),
                  (NULL, 'al''s diner',      'Weekend  Brunch  Special',         '2026-01-01T08:01:00Z'),
                  (NULL, 'AL''S  DINER',     'Catering available',               '2026-01-01T08:02:00Z'),
                  ('q',  '   ',              'First mention',                    '2026-01-01T15:00:00Z'),
                  ('q',  E'\t ',             'Second mention',                   '2026-01-01T15:01:00Z'),
                  ('p',  'Al''s Diner',      'Family owned since 1990',          '2026-01-01T09:00:00Z'),
                  ('p',  'AL''S DINER',      'Weekend brunch special',           '2026-01-01T09:01:00Z'),
                  (NULL, 'AL''S DINER',      'Open at six',                      '2026-01-01T08:03:00Z'),
                  (NULL, ' Al''s Diner',     '  Open  At  Six ',                 '2026-01-01T08:04:00Z'),
                  (NULL, 'Al''s  Diner',     'OPEN   AT SIX',                    '2026-01-01T08:05:00Z')
                """);

            await seedConn.ExecuteAsync("""
                INSERT INTO station.ad_spot (brand, title, source, pack_slug, created_at) VALUES
                  ('Acme Corp',   'Spring Sale Spot', 'owner', NULL, '2026-01-04T10:00:00Z'),
                  ('AL''S DINER', 'Brunch Spot',       'pack',  'p', '2026-01-01T09:02:00Z')
                """);

            // Round-4 finding R1' — capture the pack='p' duplicate's own id BEFORE db/46 runs: the
            // pack-identity pass deletes this row outright, so there is no premise text left to
            // re-find it by once the migration has run.
            PackAlsDinerDuplicateBriefId = await seedConn.ExecuteScalarAsync<long>(
                "SELECT id FROM station.ad_brief WHERE pack_slug = 'p' AND premise = 'Weekend brunch special'");
        }

        // The REAL db/46 script (round-2 finding F4) — RunFileInContainer throws on a non-zero
        // exit, so a failure here fails every Fact in this collection, which is the correct
        // behaviour for the arrangement step every AC in this Arc depends on.
        var db46 = Path.Combine(repoRoot, "db", "46-sponsor-migration.sh");
        database.RunFileInContainer(db46);

        await using var conn = new NpgsqlConnection(database.StationConnectionString);
        await conn.OpenAsync();

        Sponsors = (await conn.QueryAsync<SponsorRow>(
                "SELECT id AS Id, name AS Name, pack_slug AS PackSlug FROM station.sponsor ORDER BY id"))
            .Select(r => (r.Id, r.Name, r.PackSlug))
            .ToList();

        NullSponsorIdBriefCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM station.ad_brief WHERE sponsor_id IS NULL");
        NullSponsorIdSpotCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM station.ad_spot WHERE sponsor_id IS NULL");

        BriefSponsorIdIsNullable = await conn.ExecuteScalarAsync<string?>(
            "SELECT is_nullable FROM information_schema.columns " +
            "WHERE table_schema = 'station' AND table_name = 'ad_brief' AND column_name = 'sponsor_id'") ?? "YES";
        SpotSponsorIdIsNullable = await conn.ExecuteScalarAsync<string?>(
            "SELECT is_nullable FROM information_schema.columns " +
            "WHERE table_schema = 'station' AND table_name = 'ad_spot' AND column_name = 'sponsor_id'") ?? "YES";

        // Round-2 finding F2(b) — both blank-premise rows above must have folded to the SAME owner
        // sponsor and both survived (premise_key NULL is not a duplicate angle to collide on).
        // Round-3 finding N1 — one of the two premises is now a bare tab (E'\t'), the exact
        // trim-first bug case: mutating station.sponsor_fold back to trim-first turns that row's
        // premise_key into a stray ' ' instead of NULL, dropping this count from 2 to 1.
        var ownerSponsorId = Sponsors.Single(s => s.PackSlug is null && s.Name == "Acme Corp").Id;
        BlankPremiseBriefCountForOwnerSponsor = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM station.ad_brief WHERE sponsor_id = @id AND premise_key IS NULL",
            new { id = ownerSponsorId });

        // Round-3 finding N4 — every one of the six briefs seeded under the Acme spelling variants
        // (two original + two blank-premise variants + tab-padded + newline-padded, all in the
        // owner namespace) must land on this SAME sponsor, not scatter across stray sponsors with
        // edge-whitespace-corrupted names.
        AcmeOwnerSponsorBriefCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM station.ad_brief WHERE sponsor_id = @id", new { id = ownerSponsorId });

        // Round-3 finding N1 — the two literal bug-triggering brand spellings from N1's own writeup
        // (a leading tab, a trailing newline) must resolve to the SAME owner sponsor as the plain
        // spelling, proven by joining on their unique premise text rather than re-deriving name_key.
        TabAndNewlinePaddedBriefCountUnderOwnerSponsor = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM station.ad_brief WHERE sponsor_id = @id " +
            "AND premise IN ('Tab-padded brand test', 'Newline-padded brand test')",
            new { id = ownerSponsorId });

        // Round-3 finding N1 (mutation #2 — regexp_replace removed from the function) — an INTERNAL
        // double space in a premise only collapses via regexp_replace; a bare btrim never touches
        // internal runs. Pinning the exact resulting string (not just non-null) is what actually
        // catches that mutation, since the sponsor-side dedup above is shielded from it (norm_name
        // pre-collapses brand text independently of the function, before ever calling it).
        AlsDinerOwnerDoubleSpacedPremiseKey = await conn.ExecuteScalarAsync<string?>(
            "SELECT premise_key FROM station.ad_brief WHERE premise = 'Weekend  Brunch  Special'");

        // Round-4 finding P1 — every owner Al's Diner brief (original trio + the new dedupe trio)
        // must land on this SAME sponsor; the dedupe pass never moves a row to a different
        // sponsor, only ever disables it in place.
        var alsDinerOwnerSponsorId = Sponsors.Single(s => s.PackSlug is null && s.Name == "Al's Diner").Id;
        AlsDinerOwnerSponsorTotalBriefCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM station.ad_brief WHERE sponsor_id = @id",
            new { id = alsDinerOwnerSponsorId });

        // Round-4 finding P1 — the keeper: the earliest-created row (08:03) in the 'Open at six'
        // collision group, untouched by the dedupe pass (still enabled, premise unchanged).
        var keeper = await conn.QuerySingleAsync<DedupeBriefRow>(
            "SELECT id AS Id, enabled AS Enabled, premise AS Premise, sponsor_id AS SponsorId " +
            "FROM station.ad_brief WHERE sponsor_id = @id AND premise = 'Open at six'",
            new { id = alsDinerOwnerSponsorId });
        DedupeKeeperBriefId = keeper.Id;
        DedupeKeeperBriefEnabled = keeper.Enabled;
        DedupeKeeperBriefPremise = keeper.Premise;
        DedupeKeeperBriefSponsorId = keeper.SponsorId;

        // Round-4 finding R1' (supersedes round-3 finding R1's "table-wide across both passes"
        // claim) — this query now only ever catches the angle-collision pass's own disabled
        // duplicates: the pack-identity pass deletes its losing row outright, so it never carries
        // this suffix shape (see this Arc's own DedupeDisabledBriefs doc above for the full story).
        DedupeDisabledBriefs = (await conn.QueryAsync<DedupeBriefRow>(
                "SELECT id AS Id, enabled AS Enabled, premise AS Premise, sponsor_id AS SponsorId " +
                "FROM station.ad_brief WHERE premise LIKE '%disabled by db/46]'"))
            .Select(r => (r.Id, r.Enabled, r.Premise, r.SponsorId))
            .ToList();

        // Round-4 finding R1' (supersedes round-3 finding R1) — the pack-identity pass's own keeper:
        // the earliest-created row (09:00) of the pack='p' pair, untouched (still enabled, premise
        // unchanged) even though its sibling (09:01) carried a DIFFERENT premise — this pass groups
        // by (pack_slug, sponsor_id) alone. The sponsor now carries only ONE brief: the duplicate
        // was deleted, not disabled.
        var packAlsDinerSponsorId = Sponsors.Single(s => s.PackSlug == "p" && s.Name == "Al's Diner").Id;
        PackAlsDinerSponsorTotalBriefCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM station.ad_brief WHERE sponsor_id = @id",
            new { id = packAlsDinerSponsorId });
        var packKeeper = await conn.QuerySingleAsync<DedupeBriefRow>(
            "SELECT id AS Id, enabled AS Enabled, premise AS Premise, sponsor_id AS SponsorId " +
            "FROM station.ad_brief WHERE sponsor_id = @id AND premise = 'Family owned since 1990'",
            new { id = packAlsDinerSponsorId });
        PackAlsDinerKeeperBriefId = packKeeper.Id;
        PackAlsDinerKeeperBriefEnabled = packKeeper.Enabled;
        PackAlsDinerKeeperBriefPremise = packKeeper.Premise;

        // Round-4 finding R1' — the 09:01 duplicate must be GONE, both by the id captured from the
        // seed connection above and by its own premise text existing nowhere in the table (a second,
        // independent check: an id-only check alone would not catch a bug where the row survives
        // under a different id via some other path).
        PackAlsDinerDuplicateBriefExistsById = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM station.ad_brief WHERE id = @id)",
            new { id = PackAlsDinerDuplicateBriefId });
        PackAlsDinerDuplicatePremiseExistsAnywhere = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM station.ad_brief WHERE premise = 'Weekend brunch special')");

        // Round-4 finding R1' — ad_spot has no FK to ad_brief, so the pack 'p' spot's own sponsor_id
        // backfill is unaffected by the brief-side delete; it must still resolve to the same pack
        // Al's Diner sponsor as the surviving keeper brief above.
        AdSpotPackAlsDinerSponsorId = await conn.ExecuteScalarAsync<long>(
            "SELECT sponsor_id FROM station.ad_spot WHERE pack_slug = 'p' AND title = 'Brunch Spot'");

        // Round-4 finding R1' (supersedes round-3 finding R1's "nothing is ever deleted" claim,
        // which was true only of the OWNER pass): seventeen briefs seeded, fifteen present after
        // migration — the two owner Al's Diner duplicates are merely disabled, not gone, but the
        // pack-identity pass genuinely DELETES its own losing rows: the pack='p' 09:01 row AND the
        // pack='q' whitespace-only pair's later row (both 'q' rows fold to the SAME placeholder
        // "Unnamed sponsor", so they collide on (pack_slug, sponsor_id) exactly like the 'p' pair).
        TotalAdBriefCountAfterMigration = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM station.ad_brief");

        // AC5(b) — Postgres's own rendering of each generated column's expression (pg_get_expr on
        // pg_attrdef), not a guess from the source DDL: Postgres normalises source text (e.g. a bare
        // '' becomes ''::text), so these are the exact strings Postgres itself stores. Round-4 AC5
        // finding: SET search_path = station explicitly, right here, rather than leaning on the
        // fixture connection string's own Search Path=station as an implicit side effect — this
        // fact owns its own precondition for the UNqualified render both reads below depend on.
        await conn.ExecuteAsync("SET search_path = station");
        SponsorNameKeyExpr = await conn.ExecuteScalarAsync<string?>(
            "SELECT pg_get_expr(ad.adbin, ad.adrelid) FROM pg_attrdef ad " +
            "JOIN pg_attribute a ON a.attrelid = ad.adrelid AND a.attnum = ad.adnum " +
            "WHERE ad.adrelid = 'station.sponsor'::regclass AND a.attname = 'name_key'") ?? "";
        AdBriefPremiseKeyExpr = await conn.ExecuteScalarAsync<string?>(
            "SELECT pg_get_expr(ad.adbin, ad.adrelid) FROM pg_attrdef ad " +
            "JOIN pg_attribute a ON a.attrelid = ad.adrelid AND a.attnum = ad.adnum " +
            "WHERE ad.adrelid = 'station.ad_brief'::regclass AND a.attname = 'premise_key'") ?? "";

        // Round-3 finding R1 — Postgres's own rendering of the new partial unique index
        // (pg_get_indexdef on pg_index), same search_path precondition as the two reads above.
        AdBriefPackSlugSponsorIdIndexDef = await conn.ExecuteScalarAsync<string?>(
            "SELECT pg_get_indexdef(indexrelid) FROM pg_index " +
            "WHERE indexrelid = 'station.ad_brief_pack_slug_sponsor_id_key'::regclass") ?? "";

        // AC5(c) — the function's actual fold behaviour, queried live against a single mixed-
        // whitespace input carrying a leading space, an internal double-tab, and a trailing
        // space+newline — proving collapse-then-trim end to end, not just each edge case in
        // isolation.
        FoldedMixedWhitespaceAlsDiner = await conn.ExecuteScalarAsync<string?>(
            "SELECT station.sponsor_fold(@s)", new { s = " AL'S\t\tDINER \n" }) ?? "";

        SponsorCountBeforeSecondRun = Sponsors.Count;

        // AC6 — a second run must be a genuine no-op. Captured, not propagated: a failure here
        // shows up as ASecondRunExitsZero going red, not as an abort of every other Fact above.
        try
        {
            database.RunFileInContainer(db46);
        }
        catch (InvalidOperationException)
        {
            SecondRunThrew = true;
        }

        SponsorCountAfterSecondRun = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM station.sponsor");
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// Round-2 finding F1's own regression pin. Rewinds a fresh-init sponsor-era Postgres to db/45-era
/// shape, then runs EVERY db/*-migration.sh file, sorted — the exact same glob and order
/// migrate.sh's own loop uses — rather than just db/46 in isolation. F1's bug (db/06's
/// `CREATE TABLE IF NOT EXISTS station.show` is a no-op on an upgrade box, since station.show
/// already exists, so the inline sponsor_id column it declares never took effect — sponsor_id
/// was only ever added by the separate `ALTER TABLE … ADD COLUMN IF NOT EXISTS`) only surfaces
/// when the files run in migrate.sh's real order; running db/46 alone first (as
/// Story414_UpgradePathArc does, deliberately, to keep that Arc's fixture setup simple) would
/// never catch it.
/// </summary>
public sealed class Story414_FullMigrateLoopArc : IAsyncLifetime
{
    public bool LoopThrew { get; private set; }
    public string FailureMessage { get; private set; } = "";
    public bool SponsorTableExists { get; private set; }
    public bool ShowSponsorIdColumnExists { get; private set; }
    public bool AdSpotSponsorIdColumnExists { get; private set; }

    // Round-4 finding P4 — same rewind sentinel as Story414_UpgradePathArc: this Arc's own "full
    // sorted loop" starts from the exact same rewound, pre-sponsor shape.
    public long RewindSponsorTraceCount { get; private set; }
    public bool RewindAdBriefBrandColumnExists { get; private set; }

    public async Task InitializeAsync()
    {
        await using var database = await Story414SponsorDatabase.StartAsync();
        var repoRoot = RepoRootLocator.Find(AppContext.BaseDirectory);

        database.RunFileInContainer(
            Path.Combine(repoRoot, "tests", "GenWave.Host.Tests", "Support", "db46-rewind-to-db45.sh"));

        // Round-4 finding P4 — assert the rewind really landed on a pre-sponsor shape before the
        // migration loop below runs. A connection of its own: the loop's own `conn` (below) only
        // opens AFTER the loop finishes, once every migration file has already run.
        await using (var sentinelConn = new NpgsqlConnection(database.StationConnectionString))
        {
            await sentinelConn.OpenAsync();
            (RewindSponsorTraceCount, RewindAdBriefBrandColumnExists) =
                await RewindSentinel.CaptureAsync(sentinelConn);
        }

        // migrate.sh's own `db/*-migration.sh` glob, sorted the same way bash's own glob expansion
        // (and Ordinal string sort, for this all-ASCII, zero-padded filename set) already is.
        var migrationFiles = Directory
            .GetFiles(Path.Combine(repoRoot, "db"), "*-migration.sh")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        try
        {
            foreach (var file in migrationFiles)
                database.RunFileInContainer(file);
        }
        catch (InvalidOperationException ex)
        {
            LoopThrew = true;
            FailureMessage = ex.Message;
        }

        await using var conn = new NpgsqlConnection(database.StationConnectionString);
        await conn.OpenAsync();

        SponsorTableExists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM information_schema.tables " +
            "WHERE table_schema = 'station' AND table_name = 'sponsor')");
        ShowSponsorIdColumnExists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM information_schema.columns " +
            "WHERE table_schema = 'station' AND table_name = 'show' AND column_name = 'sponsor_id')");
        AdSpotSponsorIdColumnExists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM information_schema.columns " +
            "WHERE table_schema = 'station' AND table_name = 'ad_spot' AND column_name = 'sponsor_id')");
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Scenarios ────────────────────────────────────────────────────────────────────────────────────

public static class Story414_MigrationToSponsors
{
    // ── AC1 — Migration runs with the API down (index comparisons on launch.sh, round-2 finding F5) ─

    public sealed class ScenarioMigrationRunsWithTheApiDown
    {
        [Fact]
        public void PinnedFlowRunsMigrateBeforeBringingUpTheApi()
        {
            // Pinned flow: db up -> migrate.sh -> compose up (stage 1). CORE_SERVICES=(db icecast
            // engine api), and STAGE1_TARGETS is CORE_SERVICES when staged, empty (= "every
            // active-profile service") otherwise — either way this compose up is what brings up api.
            var launchText = File.ReadAllText(
                Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "launch.sh"));

            var migrateIndex = launchText.IndexOf(
                "if ! ./migrate.sh \"${MIGRATE_ARGS[@]}\"; then", StringComparison.Ordinal);
            var apiUpIndex = launchText.IndexOf(
                "if ! compose up \"${UP1_ARGS[@]}\" \"${STAGE1_TARGETS[@]}\"; then", StringComparison.Ordinal);

            Assert.True(migrateIndex >= 0, "pinned-flow migrate.sh invocation not found in launch.sh");
            Assert.True(apiUpIndex >= 0, "pinned-flow compose up (brings up api) not found in launch.sh");
            Assert.True(migrateIndex < apiUpIndex,
                "launch.sh's pinned flow must run migrate.sh BEFORE the compose up that brings up the api");
        }

        [Fact]
        public void DevFlowRunsMigrateBeforeBringingUpTheApi()
        {
            // Dev flow: compose up db -> migrate.sh --keep-going -> compose up (full stack, incl. api).
            var launchText = File.ReadAllText(
                Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "launch.sh"));

            var migrateIndex = launchText.IndexOf(
                "./migrate.sh --keep-going \"${MIGRATE_ARGS[@]}\" || true", StringComparison.Ordinal);
            var apiUpIndex = launchText.IndexOf(
                "if ! compose up \"${UP_ARGS[@]}\"; then", StringComparison.Ordinal);

            Assert.True(migrateIndex >= 0, "dev-flow migrate.sh invocation not found in launch.sh");
            Assert.True(apiUpIndex >= 0, "dev-flow compose up (full stack, brings up api) not found in launch.sh");
            Assert.True(migrateIndex < apiUpIndex,
                "launch.sh's dev flow must run migrate.sh BEFORE the compose up that brings up the api");
        }
    }

    // ── AC2 — One sponsor per distinct (pack_slug, folded brand) ─────────────────────────────────

    [Collection(Story414_UpgradePathCollection.Name)]
    public sealed class ScenarioOneSponsorPerDistinctPackSlugAndFoldedBrand
    {
        readonly Story414_UpgradePathArc arc;
        public ScenarioOneSponsorPerDistinctPackSlugAndFoldedBrand(Story414_UpgradePathArc arc)
            => this.arc = arc;

        [Fact]
        public void ExactlyFiveSponsorRowsExist()
            // Owner Acme pair + 4 edge-case spellings -> 1, owner Al's Diner trio + round-4 P1
            // dedupe trio -> 1 (same sponsor — see the P1 dedupe facts below), pack='p' pair -> 1, pack='q'
            // whitespace-only pair -> 1, over-length brand -> 1 (its fold differs from every other
            // name) = five sponsor rows from a seventeen-brief, two-spot fixture. Round-4 finding P2
            // corrects a prior claim here: round-3 finding N1/mutation #3 (reverting the backfill's
            // display-name normaliser to `left(btrim(brand),120)`) does NOT split the pack='q' pair
            // into two sponsors — both whitespace-only brands still fold to the SAME '' key, so
            // DISTINCT ON still picks one representative row, but that row's norm_name is now ''
            // (no placeholder fallback survives the revert); inserting a sponsor with name='' trips
            // station.sponsor's own sponsor_name_check and aborts the whole migration transaction,
            // reding out every Fact in this Arc's collection via InitializeAsync, not just this one
            // — see TheWhitespaceOnlyPairCollapsesToOneUnnamedSponsor below for the more targeted pin.
            => Assert.Equal(5, arc.Sponsors.Count);

        [Fact]
        public void TheMixedCaseOwnerPairBecomesOneOwnerSponsorNamedAcmeCorp()
            // Two brand spellings in the owner namespace (NULL pack_slug) fold to "acme corp";
            // canonical name is the EARLIEST spelling, 'Acme Corp' (AC3), never 'ACME CORP'.
            => Assert.Contains(arc.Sponsors, s => s.PackSlug is null && s.Name == "Acme Corp");

        [Fact]
        public void TheTabAndNewlinePaddedAcmeVariantsJoinTheSameOwnerSponsor()
            // Round-3 finding N1 — the literal bug-triggering spellings (a leading tab, a trailing
            // newline) must land on the SAME owner sponsor as the plain 'Acme Corp' spelling, not a
            // second, edge-whitespace-corrupted sponsor.
            => Assert.Equal(2, arc.TabAndNewlinePaddedBriefCountUnderOwnerSponsor);

        [Fact]
        public void AllSixAcmeVariantBriefsLandOnTheOneOwnerSponsor()
            // Two original spellings + two blank-premise variants + a tab/newline-padded pair = six
            // briefs, every one of them under the single "Acme Corp" owner sponsor (the over-length
            // brand is NOT one of the six — it lands on its own separate sponsor, see above).
            => Assert.Equal(6, arc.AcmeOwnerSponsorBriefCount);

        [Fact]
        public void ThePackPairBecomesOnePackSponsorNamedAlsDiner()
            // Two spellings under pack_slug 'p' fold the same; canonical name is the earliest,
            // "Al's Diner" (2026-01-01T09:00), not "AL'S DINER" (2026-01-01T09:01) (AC3).
            => Assert.Contains(arc.Sponsors, s => s.PackSlug == "p" && s.Name == "Al's Diner");

        [Fact]
        public void TheOwnerNamespaceAlsDinerTrioBecomesItsOwnSponsorSeparateFromThePackPair()
            // Round-3 finding N4 — the LITERAL spellings STORY-414 AC2 asks for ("Al's Diner" /
            // "al's diner" / "AL'S  DINER", the third with an internal double space), seeded in the
            // owner namespace (pack_slug NULL) alongside — but distinct from — the pack='p' pair.
            // Two sponsors named "Al's Diner" must exist, one per namespace, with different ids.
        {
            var ownerAlsDiner = arc.Sponsors.Single(s => s.PackSlug is null && s.Name == "Al's Diner");
            var packAlsDiner = arc.Sponsors.Single(s => s.PackSlug == "p" && s.Name == "Al's Diner");
            Assert.NotEqual(ownerAlsDiner.Id, packAlsDiner.Id);
        }

        [Fact]
        public void TheDoubleSpacedInternalPremiseFoldsToASingleSpacePremiseKey()
            // Round-3 finding N1 (mutation #2 — regexp_replace removed from the function): an
            // INTERNAL run of whitespace only collapses via regexp_replace; a bare btrim never
            // touches it. 'Weekend  Brunch  Special' (double spaces) must fold to a SINGLE space
            // between each word, exactly like its single-spaced counterpart on the pack='p' row.
            => Assert.Equal("weekend brunch special", arc.AlsDinerOwnerDoubleSpacedPremiseKey);

        [Fact]
        public void TheWhitespaceOnlyPairCollapsesToOneUnnamedSponsor()
            // Round-3 finding N1/N4 — two whitespace-only brands ('   ' and E'\t ') under pack='q'
            // must both fall back to the SAME "Unnamed sponsor" placeholder name (round-2 finding
            // F3). Round-4 finding P2 corrects a prior claim here: reverting the normaliser
            // (mutation #3) does NOT split this into two sponsors, one of them literally named a
            // tab character — both norm_names still fold to the SAME '' key, so DISTINCT ON still
            // picks one row, and that row's name becomes '' (no placeholder survives the revert);
            // station.sponsor's own sponsor_name_check refuses the empty name and the whole
            // migration transaction aborts instead, reding out this Fact (and every other Fact in
            // the Arc) via InitializeAsync, not via this Assert.
        {
            var qSponsors = arc.Sponsors.Where(s => s.PackSlug == "q").ToList();
            Assert.Single(qSponsors);
            Assert.Equal("Unnamed sponsor", qSponsors[0].Name);
        }

        [Fact]
        public void TwoBlankPremiseBriefsUnderTheSameSponsorBothSurvive()
            // Round-2 finding F2(b): ad_brief_sponsor_id_premise_key is plain UNIQUE, never
            // NULLS NOT DISTINCT — two blank/whitespace-only premises (round-3 finding N1: now a
            // bare tab E'\t' and two plain spaces '  ') fold to the SAME NULL premise_key and must
            // coexist under one sponsor_id, since a blank premise is not a duplicate angle (SPEC
            // F171.6). A NULLS NOT DISTINCT constraint would have refused the second of these two
            // rows during db/46's own ALTER TABLE ADD CONSTRAINT step, which would have thrown out
            // of RunFileInContainer and failed every Fact in this Arc's collection — not just this
            // one. The trim-first bug (mutation #1) also breaks this count: it leaves the tab
            // premise's fold as a non-empty ' ' instead of '', so nullif never fires.
            => Assert.Equal(2, arc.BlankPremiseBriefCountForOwnerSponsor);
    }

    // ── Round-4 finding P1 — keep, disable, disambiguate; never delete, never abort ────────────────
    // Pre-migration uniqueness on ad_brief lived on LITERAL (pack_slug, brand) — it never stopped
    // two differently-spelled briefs colliding on both the SAME sponsor and the SAME folded
    // premise. Step 9's NEW ad_brief_sponsor_id_premise_key constraint would refuse that collision
    // outright; the angle-collision dedupe pass (db/46, between the sponsor_id backfill and step 9)
    // resolves it in place instead of aborting the whole migration — this is the OWNER's own data,
    // so it is only ever disabled, never deleted. Round-4 finding R1' (supersedes round-3 finding
    // R1) adds a SECOND pass with a DIFFERENT policy, ScenarioPackBriefsAreCappedAtOnePerSponsor
    // below: pack rows are catalog content a reinstall regenerates, so that pass DELETES its losing
    // row instead of disabling it — DedupeDisabledBriefs (this Arc) therefore only ever carries this
    // (owner) pass's own disabled duplicates now, never a pack-identity one.

    [Collection(Story414_UpgradePathCollection.Name)]
    public sealed class ScenarioDuplicateAnglesAfterFoldAreKeptDisabledNotDeleted
    {
        readonly Story414_UpgradePathArc arc;
        public ScenarioDuplicateAnglesAfterFoldAreKeptDisabledNotDeleted(Story414_UpgradePathArc arc)
            => this.arc = arc;

        [Fact]
        public void AllSixOwnerAlsDinerBriefsLandOnTheOneOwnerSponsor()
            // Original owner trio (3) + the new 'Open at six' collision trio (3) = six briefs,
            // every one of them under the single owner "Al's Diner" sponsor — the dedupe pass
            // never moves a row to a different sponsor, only ever disables it in place.
            => Assert.Equal(6, arc.AlsDinerOwnerSponsorTotalBriefCount);

        [Fact]
        public void TheEarliestDuplicateStaysEnabledWithPremiseUnchanged()
            // The keeper (created 08:03, earliest of the three) is untouched by the dedupe pass:
            // still enabled, premise still the bare original text.
        {
            Assert.True(arc.DedupeKeeperBriefEnabled);
            Assert.Equal("Open at six", arc.DedupeKeeperBriefPremise);
        }

        [Fact]
        public void BothLaterDuplicatesAreDisabledWithAPremiseNoteNamingTheKeeper()
            // The two later OWNER rows (08:04, 08:05) are disabled — never deleted — and their
            // premise is suffixed with a note naming the keeper's own id, proving rn > 1 catches
            // BOTH of them, not just a single pair. Filtered to the owner sponsor's own id even
            // though (round-4 finding R1') DedupeDisabledBriefs no longer carries any pack-identity
            // row to filter out — the pack-identity pass deletes its duplicate instead of disabling
            // it (see ScenarioPackBriefsAreCappedAtOnePerSponsor).
        {
            var ownerDuplicates = arc.DedupeDisabledBriefs
                .Where(row => row.SponsorId == arc.DedupeKeeperBriefSponsorId)
                .ToList();
            Assert.Equal(2, ownerDuplicates.Count);
            Assert.All(ownerDuplicates, row =>
            {
                Assert.False(row.Enabled);
                Assert.EndsWith(", disabled by db/46]", row.Premise, StringComparison.Ordinal);
                Assert.Contains($"#{arc.DedupeKeeperBriefId}", row.Premise);
            });
        }

        [Fact]
        public void BothOwnerDuplicatesShareTheSameSponsorId()
            => Assert.All(
                arc.DedupeDisabledBriefs.Where(row => row.SponsorId == arc.DedupeKeeperBriefSponsorId),
                row => Assert.Equal(arc.DedupeKeeperBriefSponsorId, row.SponsorId));

        [Fact]
        public void ExactlyTwoDisabledDuplicatesExistFromTheOwnerPassAlone()
            // Round-4 finding R1' (supersedes round-3 finding R1's table-wide-of-three claim) — the
            // angle-collision pass's own two owner duplicates. The pack-identity pass's own duplicate
            // no longer shows up here at all: it is DELETED, not disabled (see
            // ScenarioPackBriefsAreCappedAtOnePerSponsor below).
            => Assert.Equal(2, arc.DedupeDisabledBriefs.Count);

        [Fact]
        public void TheOwnerDuplicatesAreDisabledNotDeletedTotalBriefCountIsFifteen()
            // Round-4 finding R1' (supersedes round-3 finding R1's "nothing is ever deleted" claim,
            // which held only for the OWNER pass) — seventeen briefs seeded, fifteen present after
            // migration: the two owner Al's Diner duplicates are merely disabled, not gone
            // (STORY-414: "a boring restart, not a data-loss incident" — a customer's own data is
            // the owner's to delete, not this script's), but the pack-identity pass genuinely
            // DELETES its own losing rows — TWO of them, one per (pack_slug, sponsor_id) group that
            // collides: the pack='p' pair (see ScenarioPackBriefsAreCappedAtOnePerSponsor below) AND
            // the pack='q' whitespace-only pair (both fold to the SAME placeholder "Unnamed sponsor"
            // under pack='q' — see ScenarioOneSponsorPerDistinctPackSlugAndFoldedBrand's own
            // TheWhitespaceOnlyPairCollapsesToOneUnnamedSponsor fact), which the pack-identity pass's
            // SAME (pack_slug, sponsor_id) grouping catches identically, even though this Arc asserts
            // that second deletion only through this total count rather than a dedicated fact.
            => Assert.Equal(15, arc.TotalAdBriefCountAfterMigration);
    }

    // ── Round-4 finding R1' (supersedes round-3 finding R1) — a pack has exactly one brief per
    // sponsor (SPEC F2/F3) ────────────────────────────────────────────────────────────────────────
    // The pack='p' pair (two differently-spelled brands folding to ONE sponsor, see this file's own
    // header deviation note) carries two DIFFERENT premises, so the angle-collision pass above never
    // touches it (their premise_keys differ) — only the pack-identity pass does, grouped by
    // (pack_slug, sponsor_id) alone. Without it, the NEW ad_brief_pack_slug_sponsor_id_key partial
    // index (db/46 step 9b) would refuse this pair outright and abort the whole migration. Unlike
    // the owner pass above, this pass DELETES the losing row outright (round-4 finding R1'): a pack
    // brief is catalog content a reinstall regenerates, and the partial index carries no `enabled`
    // predicate for a disabled duplicate to hide behind.

    [Collection(Story414_UpgradePathCollection.Name)]
    public sealed class ScenarioPackBriefsAreCappedAtOnePerSponsor
    {
        readonly Story414_UpgradePathArc arc;
        public ScenarioPackBriefsAreCappedAtOnePerSponsor(Story414_UpgradePathArc arc) => this.arc = arc;

        [Fact]
        public void TheEarliestPackDuplicateStaysEnabledWithPremiseUnchanged()
            // The keeper (created 09:00, earliest of the pack='p' pair) is untouched by the
            // pack-identity pass: still enabled, premise still the bare original text.
        {
            Assert.True(arc.PackAlsDinerKeeperBriefEnabled);
            Assert.Equal("Family owned since 1990", arc.PackAlsDinerKeeperBriefPremise);
        }

        [Fact]
        public void TheLaterPackDuplicateIsDeletedNotDisabled()
            // Round-4 finding R1' — the later row (09:01, 'Weekend brunch special') is GONE, proving
            // the pack-identity pass fires even though the two premises never matched (the
            // angle-collision pass alone would have left this pair alone), and that it deletes
            // rather than disables: absent BY ID (captured from the seed connection before db/46
            // ever ran) AND its premise text exists nowhere in the table — two independent checks,
            // since an id-only check alone would not catch the row surviving under a different id.
        {
            Assert.False(arc.PackAlsDinerDuplicateBriefExistsById);
            Assert.False(arc.PackAlsDinerDuplicatePremiseExistsAnywhere);
        }

        [Fact]
        public void TheDuplicateIsGoneThePackPairIsDownToOneRow()
            // The pack sponsor carries exactly one brief after migration — the keeper — not two.
            => Assert.Equal(1, arc.PackAlsDinerSponsorTotalBriefCount);

        [Fact]
        public void ThePackAdSpotStillResolvesToThePackSponsorDespiteTheBriefDelete()
            // Round-4 finding R1' — ad_spot carries no FK to ad_brief, so the pack 'p' spot's own
            // sponsor_id backfill is unaffected by the brief-side delete; it must resolve to the
            // SAME sponsor as the surviving keeper brief.
        {
            var packAlsDinerSponsorId = arc.Sponsors.Single(s => s.PackSlug == "p" && s.Name == "Al's Diner").Id;
            Assert.Equal(packAlsDinerSponsorId, arc.AdSpotPackAlsDinerSponsorId);
        }
    }

    // ── Round-3 finding R1 — the new partial unique index is text-pinned via Postgres's own render ──

    [Collection(Story414_UpgradePathCollection.Name)]
    public sealed class ScenarioThePackIdentityIndexIsPartialOnPackSlug
    {
        readonly Story414_UpgradePathArc arc;
        public ScenarioThePackIdentityIndexIsPartialOnPackSlug(Story414_UpgradePathArc arc) => this.arc = arc;

        [Fact]
        public void TheIndexPredicateExcludesOwnerRows()
            // Postgres's own rendering (pg_get_indexdef), not a guess from db/46's source DDL — the
            // WHERE clause is what makes this a PARTIAL index (a CONSTRAINT cannot be partial).
            => Assert.Equal(
                "CREATE UNIQUE INDEX ad_brief_pack_slug_sponsor_id_key ON station.ad_brief " +
                "USING btree (pack_slug, sponsor_id) WHERE (pack_slug IS NOT NULL)",
                arc.AdBriefPackSlugSponsorIdIndexDef);
    }

    // ── Round-4 finding P4 — the rewind sentinel: both Arcs really start pre-sponsor ───────────────

    [Collection(Story414_UpgradePathCollection.Name)]
    public sealed class ScenarioUpgradePathRewindLandsOnAGenuinelyPreSponsorShape
    {
        readonly Story414_UpgradePathArc arc;
        public ScenarioUpgradePathRewindLandsOnAGenuinelyPreSponsorShape(Story414_UpgradePathArc arc)
            => this.arc = arc;

        [Fact]
        public void NoSponsorNamedTraceSurvivesInTheStationSchema()
            // Zero tables/columns/functions/constraints/indexes named '%sponsor%' anywhere in the
            // station schema, captured immediately after the rewind script runs and before either
            // the fixture seed or the first db/46 run. Commenting out the rewind's own
            // DROP TABLE IF EXISTS station.sponsor (or any of its other DROPs) is exactly what
            // this sentinel exists to catch.
            => Assert.Equal(0L, arc.RewindSponsorTraceCount);

        [Fact]
        public void AdBriefBrandColumnIsBackRightAfterTheRewind()
            => Assert.True(arc.RewindAdBriefBrandColumnExists);
    }

    // ── AC5 — The fold function is centralised and correct (round-3 findings N1, AC5) ─────────────

    [Collection(Story414_UpgradePathCollection.Name)]
    public sealed class ScenarioTheFoldFunctionIsCentralizedAndCorrect
    {
        readonly Story414_UpgradePathArc arc;
        public ScenarioTheFoldFunctionIsCentralizedAndCorrect(Story414_UpgradePathArc arc) => this.arc = arc;

        [Fact]
        public void SponsorNameKeyCallsTheFoldFunction()
            // AC5(b) — Postgres's own rendering of the generated expression, via pg_get_expr on
            // pg_attrdef, not a guess from db/46's source DDL. Unqualified, not "station.
            // sponsor_fold(...)": round-4 AC5 finding — the Arc SETs search_path = station
            // explicitly on its own connection immediately before this read (own precondition, not
            // an implicit lean on the fixture connection string's own Search Path=station), and
            // ruleutils only schema-qualifies a call when it would NOT resolve unambiguously under
            // the deparsing session's own search_path.
            => Assert.Equal("sponsor_fold(name)", arc.SponsorNameKeyExpr);

        [Fact]
        public void AdBriefPremiseKeyCallsTheNullifWrappedFoldFunction()
            // AC5(b) — Postgres normalises `nullif(x, '')` source DDL into `NULLIF(x, ''::text)`;
            // unqualified call name for the same explicit search_path reason as
            // SponsorNameKeyCallsTheFoldFunction above.
            => Assert.Equal(
                "NULLIF(sponsor_fold(premise), ''::text)",
                arc.AdBriefPremiseKeyExpr);

        [Fact]
        public void TheFoldFunctionCollapsesMixedEdgeAndInternalWhitespace()
            // AC5(c) — a single live call proving collapse-then-trim end to end: a leading space, an
            // internal double-tab, and a trailing space+newline all collapse to single spaces, and
            // only the two EDGE spaces are then removed by the outer btrim.
            => Assert.Equal("al's diner", arc.FoldedMixedWhitespaceAlsDiner);
    }

    // ── AC3 — Earliest-created spelling wins (folded into the AC2 facts above) ──────────────────
    // AC3 has no facts of its own: "the canonical name is the earliest spelling" and "the fold
    // collapses to one sponsor row" are one observation, not two — TheMixedCaseOwnerPairBecomes...
    // and ThePackPairBecomes... above assert both in a single query per row.

    // ── AC4 — Zero null sponsor_id after migration ────────────────────────────────────────────────

    [Collection(Story414_UpgradePathCollection.Name)]
    public sealed class ScenarioZeroNullSponsorIdAfterMigration
    {
        readonly Story414_UpgradePathArc arc;
        public ScenarioZeroNullSponsorIdAfterMigration(Story414_UpgradePathArc arc) => this.arc = arc;

        [Fact]
        public void NoBriefRowHasANullSponsorId()
            => Assert.Equal(0L, arc.NullSponsorIdBriefCount);

        [Fact]
        public void NoSpotRowHasANullSponsorId()
            => Assert.Equal(0L, arc.NullSponsorIdSpotCount);

        [Fact]
        public void BriefSponsorIdColumnIsNotNull()
            => Assert.Equal("NO", arc.BriefSponsorIdIsNullable);

        [Fact]
        public void SpotSponsorIdColumnIsNotNull()
            => Assert.Equal("NO", arc.SpotSponsorIdIsNullable);
    }

    // ── AC5(a) — the fold function's body is text-pinned, identically, in BOTH db/46 and db/06 ─────
    // (round-2 findings F2, F9; round-3 finding N1 centralised the fold into ONE function, so the
    // pin now targets that one function body rather than two independently-duplicated inline
    // expressions — see this file's own header deviation note).

    public sealed class ScenarioTheFoldExpressionIsTextPinned
    {
        // Round-3 finding N1: COLLAPSE FIRST (regexp_replace), TRIM SECOND (the outer btrim) — bare
        // btrim(x) strips SPACES ONLY (it is exactly btrim(x, ' '), never "every Unicode whitespace
        // character"), so trimming before collapsing leaves an edge tab/newline uncollapsed until
        // regexp_replace turns it into a stray internal-looking space nothing then removes.
        const string FoldFunctionBody = "btrim(regexp_replace(lower($1), '\\s+', ' ', 'g'))";

        [Fact]
        public void Db46CarriesTheFoldFunctionBody()
        {
            var db46 = File.ReadAllText(
                Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "db", "46-sponsor-migration.sh"));
            Assert.Contains(FoldFunctionBody, db46);
        }

        [Fact]
        public void Db06CarriesTheIdenticalFoldFunctionBody()
            // The fresh-init mirror must define the SAME function body as db/46's own step 0 — a
            // fresh install and an upgraded box must fold names identically.
        {
            var db06 = File.ReadAllText(
                Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "db", "06-station-settings-migration.sh"));
            Assert.Contains(FoldFunctionBody, db06);
        }

        // ── Round-3 finding N6 — drift tripwire: the raw collapse expression must appear in CODE
        // (never in a comment, which is not exercised by any planner) exactly once in db/06 (the
        // function body) and exactly twice in db/46 (the function body + the ONE display-name
        // normaliser in the backfill, which must preserve case and so cannot call the function
        // directly). A future edit that hand-copies a THIRD raw collapse expression anywhere in
        // either file — reintroducing the exact drift N1 fixed — flips these counts and goes red.

        [Fact]
        public void Db06ContainsExactlyOneRawCollapseExpressionInCode()
            => Assert.Equal(1, CountRawCollapseExpressionsInCode("db", "06-station-settings-migration.sh"));

        [Fact]
        public void Db46ContainsExactlyTwoRawCollapseExpressionsInCode()
            => Assert.Equal(2, CountRawCollapseExpressionsInCode("db", "46-sponsor-migration.sh"));

        static int CountRawCollapseExpressionsInCode(params string[] relativePathParts)
        {
            const string needle = "'\\s+', ' ', 'g'";
            var path = Path.Combine(
                [RepoRootLocator.Find(AppContext.BaseDirectory), .. relativePathParts]);
            return File.ReadAllLines(path)
                .Select(line => line.TrimStart(' ', '\t'))
                .Count(line =>
                    !line.StartsWith('#') && !line.StartsWith("--", StringComparison.Ordinal) &&
                    line.Contains(needle, StringComparison.Ordinal));
        }
    }

    // ── AC6 — db/46 is idempotent ────────────────────────────────────────────────────────────────

    [Collection(Story414_UpgradePathCollection.Name)]
    public sealed class ScenarioIdempotent
    {
        readonly Story414_UpgradePathArc arc;
        public ScenarioIdempotent(Story414_UpgradePathArc arc) => this.arc = arc;

        [Fact]
        public void ASecondRunExitsZero()
            => Assert.False(arc.SecondRunThrew,
                "db/46 threw on second run — at least one step is not idempotent");

        [Fact]
        public void ASecondRunChangesNoSponsorRow()
            => Assert.Equal(arc.SponsorCountBeforeSecondRun, arc.SponsorCountAfterSecondRun);
    }

    // ── Round-2 finding F1 regression pin — the FULL sorted migrate.sh loop, not db/46 alone ─────

    [Collection(Story414_FullMigrateLoopCollection.Name)]
    public sealed class ScenarioTheFullSortedMigrateLoopConverges
    {
        readonly Story414_FullMigrateLoopArc arc;
        public ScenarioTheFullSortedMigrateLoopConverges(Story414_FullMigrateLoopArc arc) => this.arc = arc;

        [Fact]
        public void TheLoopDoesNotThrow()
            => Assert.False(arc.LoopThrew, arc.FailureMessage);

        [Fact]
        public void StationSponsorTableExistsAfterTheLoop()
            => Assert.True(arc.SponsorTableExists);

        [Fact]
        public void ShowCarriesSponsorIdAfterTheLoop()
            => Assert.True(arc.ShowSponsorIdColumnExists);

        [Fact]
        public void AdSpotCarriesSponsorIdAfterTheLoop()
            => Assert.True(arc.AdSpotSponsorIdColumnExists);
    }

    // ── Round-4 finding P4 — the rewind sentinel, this Arc's own copy ──────────────────────────────

    [Collection(Story414_FullMigrateLoopCollection.Name)]
    public sealed class ScenarioFullMigrateLoopRewindLandsOnAGenuinelyPreSponsorShape
    {
        readonly Story414_FullMigrateLoopArc arc;
        public ScenarioFullMigrateLoopRewindLandsOnAGenuinelyPreSponsorShape(Story414_FullMigrateLoopArc arc)
            => this.arc = arc;

        [Fact]
        public void NoSponsorNamedTraceSurvivesInTheStationSchema()
            => Assert.Equal(0L, arc.RewindSponsorTraceCount);

        [Fact]
        public void AdBriefBrandColumnIsBackRightAfterTheRewind()
            => Assert.True(arc.RewindAdBriefBrandColumnExists);
    }
}

// ── Ephemeral DB (shared by both Arcs via file-scoped type) ──────────────────────────────────────

file sealed class Story414SponsorDatabase : EphemeralStationDatabase
{
    Story414SponsorDatabase(string project, string composeFile, string lib, string station)
        : base(project, composeFile, lib, station) { }

    public static async Task<Story414SponsorDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t414");
        var db = new Story414SponsorDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}
