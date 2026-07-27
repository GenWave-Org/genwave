// STORY-251 — Know which tracks are explicit (SPEC F95.2, F95.3, F95.5, PLAN T110/T112/T113/T115)
//
// BDD specification — xUnit, pending. db/26 schema + the layered tag → LLM sweep → operator
// pipeline, driven against a real Postgres with the LLM faked at the HTTP boundary (T72
// mood-tagger idiom). The operator override endpoint's wire facts (T115) drive the real
// admin route via the Host factory.
//
// T110 is Postgres-backed (Category=Integration) via DatabaseCollection, mirroring the
// SchemaAndMigration family's shape (Story039_CatalogWriteColumnsSchemaAndMigration and
// Story237_ProvenanceSchemaAndMigration are the closest siblings): ScenarioTheSpineCarriesTheFlag
// asserts the column shape db/01-library.sh's fresh init leaves in place and proves the
// explicit_source CHECK accepts its vocabulary; ScenarioMigrationAddsColumnsInPlace drops both
// columns, runs db/26-explicit-classification-migration.sh in the compose testdb container, and
// asserts the resulting shape, the CHECK's teeth, and a pre-existing row's NULL/NULL;
// ScenarioRejectingUnknownSources and ScenarioMigrationIsIdempotent are this family's sad paths.

using System.Net;
using System.Net.Http.Json;
using Dapper;
using GenWave.MediaLibrary.Enrich;
using GenWave.MediaLibrary.Tests.Fakes;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureExplicitClassification
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Returns (data_type, is_nullable) for the named column on library.media, or null when the
    /// column does not exist. Mirrors Story039/Story237's own information_schema helpers.
    /// </summary>
    static async Task<(string DataType, string IsNullable)?> QueryColumnAsync(DatabaseFixture db, string columnName)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var row = await conn.QuerySingleOrDefaultAsync<(string data_type, string is_nullable)>(
            """
            select data_type, is_nullable from information_schema.columns
            where table_schema = 'library' and table_name = 'media' and column_name = @column
            """,
            new { column = columnName });

        return row == default ? null : (row.data_type, row.is_nullable);
    }

    static async Task<long> InsertMediaRowAsync(DatabaseFixture db, string path)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            insert into library.media (path, format, size_bytes, mtime, state, library_id)
            values (@path, 'flac', 1024, now(), 'discovered', 1)
            returning id
            """,
            new { path });
    }

    static void RunMigrationScript(DatabaseFixture db) =>
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "26-explicit-classification-migration.sh"));

    /// <summary>Reads back the tag pass's own two columns for one row. Shared by every scenario in
    /// this feature that drives a real file through <see cref="EnrichmentService.EnrichOneAsync"/>
    /// and asserts what landed.</summary>
    static async Task<(bool? Explicit, string? ExplicitSource)> ExplicitColumnsOfAsync(DatabaseFixture f, long id)
    {
        await using var conn = await f.DataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<(bool?, string?)>(
            "select explicit, explicit_source from library.media where id = @id", new { id });
    }

    /// <summary>Seeds <c>explicit</c>/<c>explicit_source</c> directly in Postgres — stands in for a
    /// prior tag/LLM/operator pass that isn't (yet, or ever, in this test) reached via the real code
    /// path, e.g. T113's LLM sweep or T115's operator override endpoint.</summary>
    static async Task SeedExplicitAsync(DatabaseFixture db, long id, bool value, string source)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "update library.media set explicit = @value, explicit_source = @source where id = @id",
            new { id, value, source });
    }

    /// <summary>Reads back the T113 sweep's own re-claim gate for one row.</summary>
    static async Task<DateTime?> ExplicitLlmMissedAtOfAsync(DatabaseFixture f, long id)
    {
        await using var conn = await f.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<DateTime?>(
            "select explicit_llm_missed_at from library.media where id = @id", new { id });
    }

    /// <summary>
    /// A fake chat-completions endpoint returning the SAME <paramref name="rawContent"/> for every
    /// request, regardless of which track it was asked about. Mirrors Story216's own MoodHandler.
    /// </summary>
    static FakeHttpMessageHandler ExplicitHandler(string rawContent) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { choices = new[] { new { message = new { content = rawContent } } } }),
        }));

    /// <summary>
    /// A fake chat-completions endpoint that never completes a round trip — every request gets a
    /// non-2xx status, which <c>OllamaExplicitClassifier</c> collapses to a failed call
    /// (<c>LastCallFailed</c> true), never a genuine "unknown" verdict.
    /// </summary>
    static FakeHttpMessageHandler ExplicitFailingHandler() =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

    /// <summary>
    /// A fake chat-completions endpoint whose answer depends on which track the request names —
    /// keyed by a substring of the request's own user-prompt content (the classifier embeds
    /// <c>Title: {title}</c> verbatim), so a single handler can serve a batch of more than one row
    /// with different outcomes. Mirrors Story216's own MoodHandlerByTitle exactly.
    /// </summary>
    static FakeHttpMessageHandler ExplicitHandlerByTitle(IReadOnlyDictionary<string, string> responsesByTitle) =>
        new(async (request, ct) =>
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            var (_, rawContent) = responsesByTitle.First(kv => body.Contains(kv.Key));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { choices = new[] { new { message = new { content = rawContent } } } }),
            };
        });

    // ---------------------------------------------------------------------
    // HAPPY PATH — fresh init (db/01-library.sh)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheSpineCarriesTheFlag(DatabaseFixture db)
    {
        // Given db/26 applied (F95.2).

        [Fact]
        public async Task ExplicitBooleanExistsWithNullAsUnknown()
        {
            // A freshly discovered row is unclassified — NULL, never a sentinel false.
            await db.ResetAsync();

            var column = await QueryColumnAsync(db, "explicit");
            Assert.NotNull(column);
            Assert.Equal("boolean", column.Value.DataType);
            Assert.Equal("YES", column.Value.IsNullable);

            var mediaId = await InsertMediaRowAsync(db, "/test/explicit-flag-unknown.flac");

            await using var conn = await db.DataSource.OpenConnectionAsync();
            var explicitValue = await conn.ExecuteScalarAsync<bool?>(
                "select explicit from library.media where id = @mediaId", new { mediaId });
            Assert.Null(explicitValue);
        }

        [Fact]
        public async Task ExplicitSourceIsConstrainedToTagLlmOperator()
        {
            await db.ResetAsync();

            var column = await QueryColumnAsync(db, "explicit_source");
            Assert.NotNull(column);
            Assert.Equal("text", column.Value.DataType);
            Assert.Equal("YES", column.Value.IsNullable);

            var mediaId = await InsertMediaRowAsync(db, "/test/explicit-source-check.flac");

            await using var conn = await db.DataSource.OpenConnectionAsync();

            // Each of the three known origins (F95.3) is accepted by the CHECK.
            foreach (var source in new[] { "tag", "llm", "operator" })
            {
                await conn.ExecuteAsync(
                    "update library.media set explicit = true, explicit_source = @source where id = @mediaId",
                    new { mediaId, source });

                var stored = await conn.ExecuteScalarAsync<string>(
                    "select explicit_source from library.media where id = @mediaId", new { mediaId });
                Assert.Equal(source, stored);
            }
        }

        [Fact]
        public async Task ExplicitLlmMissedAtExistsAsANullableTimestamp()
        {
            // The T113 sweep's own re-claim gate (mirrors mood_tag_missed_at/year_lookup_missed_at,
            // SPEC F76, F85.4) — nullable timestamptz, NULL until the sweep ever misses this row.
            await db.ResetAsync();

            var column = await QueryColumnAsync(db, "explicit_llm_missed_at");
            Assert.NotNull(column);
            Assert.Equal("timestamp with time zone", column.Value.DataType);
            Assert.Equal("YES", column.Value.IsNullable);

            var mediaId = await InsertMediaRowAsync(db, "/test/explicit-llm-missed-at-unknown.flac");
            Assert.Null(await ExplicitLlmMissedAtOfAsync(db, mediaId));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — in-place migration (db/26-explicit-classification-migration.sh)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrationAddsColumnsInPlace(DatabaseFixture db)
    {
        [Fact]
        public async Task MigrationAddsBothColumnsWithTheCheckAndLeavesExistingRowsNullNull()
        {
            // Simulate a pre-migration database by dropping all three explicit-classification columns
            // (the flag/source pair plus the sweep's own re-claim gate, T113).
            await using var conn = await db.DataSource.OpenConnectionAsync();
            await conn.ExecuteAsync(
                "alter table library.media " +
                "drop column if exists explicit, drop column if exists explicit_source, " +
                "drop column if exists explicit_llm_missed_at");

            Assert.Null(await QueryColumnAsync(db, "explicit"));
            Assert.Null(await QueryColumnAsync(db, "explicit_llm_missed_at"));

            // Insert a row while the columns do not yet exist.
            var mediaId = await InsertMediaRowAsync(db, "/test/explicit-migration-preexisting.flac");

            RunMigrationScript(db);

            var explicitCol = await QueryColumnAsync(db, "explicit");
            Assert.NotNull(explicitCol);
            Assert.Equal("boolean", explicitCol.Value.DataType);
            Assert.Equal("YES", explicitCol.Value.IsNullable);

            var explicitSourceCol = await QueryColumnAsync(db, "explicit_source");
            Assert.NotNull(explicitSourceCol);
            Assert.Equal("text", explicitSourceCol.Value.DataType);
            Assert.Equal("YES", explicitSourceCol.Value.IsNullable);

            var explicitLlmMissedAtCol = await QueryColumnAsync(db, "explicit_llm_missed_at");
            Assert.NotNull(explicitLlmMissedAtCol);
            Assert.Equal("timestamp with time zone", explicitLlmMissedAtCol.Value.DataType);
            Assert.Equal("YES", explicitLlmMissedAtCol.Value.IsNullable);

            // The CHECK has teeth — anything outside the vocabulary (F95.3) is rejected.
            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                "update library.media set explicit_source = 'guess' where id = @mediaId", new { mediaId }));

            // The pre-existing row is untouched by the migration — NULL/NULL, never backfilled — and
            // the re-claim gate is untouched too.
            var explicitValue = await conn.ExecuteScalarAsync<bool?>(
                "select explicit from library.media where id = @mediaId", new { mediaId });
            Assert.Null(explicitValue);

            var explicitSourceValue = await conn.ExecuteScalarAsync<string?>(
                "select explicit_source from library.media where id = @mediaId", new { mediaId });
            Assert.Null(explicitSourceValue);

            Assert.Null(await ExplicitLlmMissedAtOfAsync(db, mediaId));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — unknown sources are rejected by the CHECK
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRejectingUnknownSources(DatabaseFixture db)
    {
        [Fact]
        public async Task AnUnknownSourceIsRejectedByTheCheckConstraint()
        {
            await db.ResetAsync();
            var mediaId = await InsertMediaRowAsync(db, "/test/explicit-source-rejected.flac");

            await using var conn = await db.DataSource.OpenConnectionAsync();

            // Anything outside the vocabulary (tag, llm, operator — F95.3) is rejected by the CHECK.
            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                "update library.media set explicit_source = 'guess' where id = @mediaId", new { mediaId }));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — idempotency
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrationIsIdempotent(DatabaseFixture db)
    {
        [Fact]
        public async Task RerunningTheMigrationExitsZeroAndLeavesTheShapeUnchanged()
        {
            // Columns already exist (from db/01's fresh init or a prior migration run) — all three,
            // including the T113 re-claim gate.
            var before = await QueryColumnAsync(db, "explicit");
            Assert.NotNull(before);
            var missedAtBefore = await QueryColumnAsync(db, "explicit_llm_missed_at");
            Assert.NotNull(missedAtBefore);

            // First run — succeeds even if columns already exist (ADD COLUMN IF NOT EXISTS).
            RunMigrationScript(db);

            // Second run — RunFileInContainer throws on a nonzero exit code, so simply RETURNING here
            // (rather than throwing) is itself the proof this run exited 0.
            RunMigrationScript(db);

            var after = await QueryColumnAsync(db, "explicit");
            Assert.Equal(before, after);

            var missedAtAfter = await QueryColumnAsync(db, "explicit_llm_missed_at");
            Assert.Equal(missedAtBefore, missedAtAfter);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAdvisoryTagsStampFirst(DatabaseFixture db)
    {
        // Given a file whose metadata carries an explicit/advisory flag, When enrichment runs (F95.3).

        [Fact]
        public async Task ExplicitIsStampedWithSourceTag()
        {
            // Drives the real enrichment path end to end (TestMedia → Enricher's TagLib read →
            // MediaRepository.WriteEnrichmentAsync), never a raw SQL seed — proves the tag pass this
            // task adds, not just the schema T110 already pinned. Both real-world advisory
            // conventions (F95.3) are exercised: an ID3v2 TXXX user-text frame for mp3, a Vorbis
            // comment field for flac — the same ITUNESADVISORY key, value "1" = explicit.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var repo = Harness.Repo(db);

                var mp3Path = TestMedia.CreateTone(dir, "explicit.mp3", itunesAdvisory: "1");
                var mp3Id = await repo.InsertDiscoveredAsync(
                    mp3Path, "mp3", new FileInfo(mp3Path).Length, Harness.Mtime, CancellationToken.None);
                await Harness.Enrichment(repo).EnrichOneAsync(mp3Id, CancellationToken.None);

                var mp3Columns = await ExplicitColumnsOfAsync(db, mp3Id);
                Assert.Equal(true, mp3Columns.Explicit);
                Assert.Equal("tag", mp3Columns.ExplicitSource);

                var flacPath = TestMedia.CreateTone(dir, "explicit.flac", itunesAdvisory: "1");
                var flacId = await repo.InsertDiscoveredAsync(
                    flacPath, "flac", new FileInfo(flacPath).Length, Harness.Mtime, CancellationToken.None);
                await Harness.Enrichment(repo).EnrichOneAsync(flacId, CancellationToken.None);

                var flacColumns = await ExplicitColumnsOfAsync(db, flacId);
                Assert.Equal(true, flacColumns.Explicit);
                Assert.Equal("tag", flacColumns.ExplicitSource);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task UntaggedFilesStayNull()
        {
            // No advisory tag at all (SPEC F95.2) — a miss, never a sentinel false: both columns
            // stay NULL exactly like a freshly discovered, unenriched row.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var repo = Harness.Repo(db);
                var path = TestMedia.CreateTone(dir, "plain.flac");
                var id = await repo.InsertDiscoveredAsync(
                    path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                await Harness.Enrichment(repo).EnrichOneAsync(id, CancellationToken.None);

                var columns = await ExplicitColumnsOfAsync(db, id);
                Assert.Null(columns.Explicit);
                Assert.Null(columns.ExplicitSource);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task CleanAdvisoryTagStampsExplicitFalseWithSourceTag()
        {
            // ITUNESADVISORY=2 ("clean") is itself a positive result (F95.3), not a miss like an
            // absent tag — it must land explicit=false/'tag', never stay NULL.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var repo = Harness.Repo(db);
                var path = TestMedia.CreateTone(dir, "clean.mp3", itunesAdvisory: "2");
                var id = await repo.InsertDiscoveredAsync(
                    path, "mp3", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                await Harness.Enrichment(repo).EnrichOneAsync(id, CancellationToken.None);

                var columns = await ExplicitColumnsOfAsync(db, id);
                Assert.Equal(false, columns.Explicit);
                Assert.Equal("tag", columns.ExplicitSource);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task ZeroAdvisoryValueIsAMissAndStaysNull()
        {
            // ITUNESADVISORY=0 is outside the tag pass's vocabulary ("1"/"2" only, F95.3) — a miss,
            // exactly like an absent tag: stays NULL/NULL, never stamped.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var repo = Harness.Repo(db);
                var path = TestMedia.CreateTone(dir, "zero-advisory.mp3", itunesAdvisory: "0");
                var id = await repo.InsertDiscoveredAsync(
                    path, "mp3", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                await Harness.Enrichment(repo).EnrichOneAsync(id, CancellationToken.None);

                var columns = await ExplicitColumnsOfAsync(db, id);
                Assert.Null(columns.Explicit);
                Assert.Null(columns.ExplicitSource);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task ChangedAdvisoryTagRestampsOnReEnrich()
        {
            // A prior LLM sweep (T113, not yet built) stamped this row explicit=true/'llm' — seeded
            // directly since no real sweep exists to drive. The file's own ITUNESADVISORY tag then
            // changes on disk (1 -> 2) and the row is rediscovered for re-enrichment: the tag pass is
            // the ONLY place a re-scan lands (WriteEnrichmentAsync's own doc comment), so it must
            // supersede the LLM guess — stronger evidence wins regardless of the prior source, this
            // is the llm -> tag supersede specifically.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var repo = Harness.Repo(db);
                var path = TestMedia.CreateTone(dir, "llm-then-tag.mp3", itunesAdvisory: "1");
                var id = await repo.InsertDiscoveredAsync(
                    path, "mp3", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                await SeedExplicitAsync(db, id, value: true, source: "llm");

                // The advisory tag flips on disk (explicit -> clean) — same file, rewritten.
                TestMedia.CreateTone(dir, "llm-then-tag.mp3", itunesAdvisory: "2");

                // Mirrors Story041's re-enrich idiom: reset state to discovered before re-running
                // EnrichOneAsync directly (EnrichOneAsync itself does not filter on state).
                await using (var conn = await db.DataSource.OpenConnectionAsync())
                    await conn.ExecuteAsync("update library.media set state = 'discovered' where id = @id", new { id });

                await Harness.Enrichment(repo).EnrichOneAsync(id, CancellationToken.None);

                var columns = await ExplicitColumnsOfAsync(db, id);
                Assert.Equal(false, columns.Explicit);
                Assert.Equal("tag", columns.ExplicitSource);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheSweepCoversTheRest(DatabaseFixture db)
    {
        // Given unclassified tracks and a configured LLM, When the offline batch pass runs (F95.3).

        [Fact]
        public async Task YesAndNoStampSourceLlm()
        {
            // Two unclassified ready rows, one that should land explicit/one that shouldn't — a
            // single tick classifies both, each landing source 'llm' regardless of the verdict.
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var yesId = await repo.InsertDiscoveredAsync("/explicit-sweep/yes.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(yesId, Harness.ReadyResultWith(title: "Filthy Words", artist: "The Testers"), CancellationToken.None);

            var noId = await repo.InsertDiscoveredAsync("/explicit-sweep/no.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(noId, Harness.ReadyResultWith(title: "Sunny Afternoon", artist: "The Testers"), CancellationToken.None);

            var handler = ExplicitHandlerByTitle(new Dictionary<string, string>
            {
                ["Filthy Words"]    = "yes",
                ["Sunny Afternoon"] = "no",
            });
            var svc = Harness.BackfillExplicitClassificationWith(repo, handler);

            await svc.BackfillExplicitClassificationAsync(CancellationToken.None);

            var yesColumns = await ExplicitColumnsOfAsync(db, yesId);
            Assert.Equal(true, yesColumns.Explicit);
            Assert.Equal("llm", yesColumns.ExplicitSource);

            var noColumns = await ExplicitColumnsOfAsync(db, noId);
            Assert.Equal(false, noColumns.Explicit);
            Assert.Equal("llm", noColumns.ExplicitSource);
        }

        [Fact]
        public async Task UnknownStampsAMissNeverAPartialWrite()
        {
            // A completed round trip that answers "unknown" is a genuine miss (F95.3): the re-claim
            // gate is stamped so it is never re-asked, but explicit/explicit_source stay NULL/NULL —
            // never a partial write of just one of the pair.
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/explicit-sweep/unknown.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(title: "Ambiguous Title", artist: "Nobody"), CancellationToken.None);

            var handler = ExplicitHandler("unknown");
            var svc = Harness.BackfillExplicitClassificationWith(repo, handler);

            await svc.BackfillExplicitClassificationAsync(CancellationToken.None);

            var columns = await ExplicitColumnsOfAsync(db, id);
            Assert.Null(columns.Explicit);
            Assert.Null(columns.ExplicitSource);
            Assert.NotNull(await ExplicitLlmMissedAtOfAsync(db, id));
        }

        [Fact]
        public async Task AReplyNamingTheVerdictMidSentenceIsAMissNeverAnInvertedVerdict()
        {
            // ExplicitClassificationParser is an EXACT-MATCH parse, not a "scan for the first
            // recognizable word" one (T113 review finding): a reply like this track's own real title
            // embedded in a sentence — "No Diggity: yes" — must never scan-match "no" first and stamp
            // an inverted (and permanently wrong) verdict. The whole reply doesn't equal "yes", "no",
            // or "unknown", so it is a genuine miss: the re-claim gate is stamped, both columns stay
            // NULL/NULL.
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/explicit-sweep/no-diggity.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(title: "No Diggity", artist: "Blackstreet"), CancellationToken.None);

            var handler = ExplicitHandler("No Diggity: yes");
            var svc = Harness.BackfillExplicitClassificationWith(repo, handler);

            await svc.BackfillExplicitClassificationAsync(CancellationToken.None);

            var columns = await ExplicitColumnsOfAsync(db, id);
            Assert.Null(columns.Explicit);
            Assert.Null(columns.ExplicitSource);
            Assert.NotNull(await ExplicitLlmMissedAtOfAsync(db, id));
        }

        [Fact]
        public async Task AVerboseNonAnswerIsAMissNeverAPartialWrite()
        {
            // A completed round trip that ignores the constrained-output contract entirely — a
            // sentence containing no exact yes/no/unknown token — is the SAME miss outcome, never a
            // guess extracted from somewhere inside the prose.
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/explicit-sweep/verbose.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(title: "Ambiguous Title", artist: "Nobody"), CancellationToken.None);

            var handler = ExplicitHandler("There is no way to tell from the title alone.");
            var svc = Harness.BackfillExplicitClassificationWith(repo, handler);

            await svc.BackfillExplicitClassificationAsync(CancellationToken.None);

            var columns = await ExplicitColumnsOfAsync(db, id);
            Assert.Null(columns.Explicit);
            Assert.Null(columns.ExplicitSource);
            Assert.NotNull(await ExplicitLlmMissedAtOfAsync(db, id));
        }

        [Fact]
        public async Task AlreadyClassifiedRowsAreNotReAsked()
        {
            // A row the tag pass already stamped (explicit_source = 'tag') is excluded from the
            // sweep's own claim query entirely — not merely "answered the same way", genuinely never
            // asked: a handler that WOULD flip the verdict if ever queried proves zero requests land.
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/explicit-sweep/already-tagged.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(title: "Already Classified", artist: "Someone"), CancellationToken.None);
            await SeedExplicitAsync(db, id, value: true, source: "tag");

            var handler = ExplicitHandler("no");   // would flip the verdict if ever asked — must not be
            var svc = Harness.BackfillExplicitClassificationWith(repo, handler);

            await svc.BackfillExplicitClassificationAsync(CancellationToken.None);

            Assert.Empty(handler.Requests);
            var columns = await ExplicitColumnsOfAsync(db, id);
            Assert.Equal(true, columns.Explicit);
            Assert.Equal("tag", columns.ExplicitSource);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheOperatorAlwaysWins(DatabaseFixture db)
    {
        // Given an operator override (source operator) (F95.3), set via the real admin endpoint (T115).

        [Fact(Skip = "Pending (T115)")]
        public void OverrideEndpointStampsSourceOperator() { }

        [Fact]
        public async Task LaterSweepsNeverOverwriteOperatorRows()
        {
            // An operator-owned row (explicit_source = 'operator') is excluded from the sweep's own
            // claim query entirely — a handler that WOULD flip the verdict if ever queried proves
            // zero requests land, and the operator's stamp survives untouched.
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/explicit-sweep/operator-owned.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(title: "Operator Owned", artist: "Someone"), CancellationToken.None);
            await SeedExplicitAsync(db, id, value: false, source: "operator");

            var handler = ExplicitHandler("yes");   // would flip the verdict if ever asked — must not be
            var svc = Harness.BackfillExplicitClassificationWith(repo, handler);

            await svc.BackfillExplicitClassificationAsync(CancellationToken.None);

            Assert.Empty(handler.Requests);
            var columns = await ExplicitColumnsOfAsync(db, id);
            Assert.Equal(false, columns.Explicit);
            Assert.Equal("operator", columns.ExplicitSource);
        }

        [Fact]
        public async Task TagPassNeverOverwritesOperatorRows()
        {
            // The T115 override endpoint doesn't exist yet, but the write-guard this task (T112) pins
            // lives in WriteEnrichmentAsync's own CASE, not the endpoint — seed operator ownership
            // directly in Postgres, then drive a REAL file with a contradicting advisory tag through
            // the real enrichment path (Harness.Enrichment -> EnrichOneAsync) and assert the
            // operator's stamp survives untouched.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var repo = Harness.Repo(db);
                var path = TestMedia.CreateTone(dir, "operator-owned.mp3", itunesAdvisory: "2");
                var id = await repo.InsertDiscoveredAsync(
                    path, "mp3", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                await SeedExplicitAsync(db, id, value: true, source: "operator");

                await Harness.Enrichment(repo).EnrichOneAsync(id, CancellationToken.None);

                var columns = await ExplicitColumnsOfAsync(db, id);
                Assert.Equal(true, columns.Explicit);
                Assert.Equal("operator", columns.ExplicitSource);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    public sealed class ScenarioNeverPlayStaysOrthogonal
    {
        // Given a track under a never-play verdict (F95.5): the flag classifies, the verdict rules.

        [Fact(Skip = "Pending (T115)")]
        public void VerdictOperatesUnchangedRegardlessOfClassification() { }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioLlmDownSkipsCleanly(DatabaseFixture db)
    {
        // Sad path — LLM unreachable (F95.3, F69 pattern).

        [Fact]
        public async Task SweepSkipsWithASingleLogLineAndNoPartialStamps()
        {
            // degraded/off/unconfigured => clean skip, single line, no per-track noise (F95.3,
            // mirrors F85.3) — an eligible candidate exists (proving this is a genuine skip, not
            // "nothing to do"), and no column is touched at all: not even the miss stamp.
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/explicit-sweep/sad-path.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(title: "Eligible Track", artist: "Someone"), CancellationToken.None);

            var handler = ExplicitHandler("yes");   // would succeed if ever called — it must not be
            var gate = new FakeLlmBatchGate(allowed: false, reason: "LLM degraded (Soft)");
            var logger = new CapturingLogger<EnrichmentService>();
            var svc = Harness.BackfillExplicitClassificationWith(repo, handler, gate, logger);

            await svc.BackfillExplicitClassificationAsync(CancellationToken.None);

            Assert.Empty(handler.Requests);
            var columns = await ExplicitColumnsOfAsync(db, id);
            Assert.Null(columns.Explicit);
            Assert.Null(columns.ExplicitSource);
            Assert.Null(await ExplicitLlmMissedAtOfAsync(db, id));

            // Exactly ONE line for the whole batch — never per-track.
            var line = Assert.Single(logger.Informational);
            Assert.Contains("degraded", line, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTransientOutageNeverStampsAMiss(DatabaseFixture db)
    {
        // Sad path — a failed HTTP round trip on an individual row (SPEC F95.3, mirrors F76.2's
        // failed-vs-missed split, Story144's ScenarioFailuresStampAndNeverBlock idiom). Distinct from
        // ScenarioLlmDownSkipsCleanly above, which is the GATE refusing the whole batch before any
        // call is even made — this is the classifier itself failing one call mid-batch.

        [Fact]
        public async Task A500ResponseLeavesAllThreeColumnsNull()
        {
            // A transient outage (IExplicitClassifierDiagnostics.LastCallFailed) must never stamp the
            // re-claim gate — that would treat a passing outage as a permanent "can't tell" verdict.
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/explicit-sweep/outage.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(title: "Outage Track", artist: "Someone"), CancellationToken.None);

            var handler = ExplicitFailingHandler();
            var svc = Harness.BackfillExplicitClassificationWith(repo, handler);

            await svc.BackfillExplicitClassificationAsync(CancellationToken.None);

            Assert.Single(handler.Requests);
            var columns = await ExplicitColumnsOfAsync(db, id);
            Assert.Null(columns.Explicit);
            Assert.Null(columns.ExplicitSource);
            Assert.Null(await ExplicitLlmMissedAtOfAsync(db, id));
        }

        [Fact]
        public async Task AFailedRowIsReclaimedOnTheVeryNextTick()
        {
            // The row was never miss-stamped (it merely failed a round trip) — a FRESH handler on a
            // second tick that WOULD succeed proves it is reclaimed and asked again, exactly like
            // Story144's own year-lookup outage-retry fact.
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/explicit-sweep/outage-retry.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(title: "Outage Retry Track", artist: "Someone"), CancellationToken.None);

            var firstHandler = ExplicitFailingHandler();
            var firstRun = Harness.BackfillExplicitClassificationWith(repo, firstHandler);
            await firstRun.BackfillExplicitClassificationAsync(CancellationToken.None);

            var secondHandler = ExplicitHandler("yes");
            var secondRun = Harness.BackfillExplicitClassificationWith(repo, secondHandler);
            await secondRun.BackfillExplicitClassificationAsync(CancellationToken.None);

            Assert.Single(secondHandler.Requests);
            var columns = await ExplicitColumnsOfAsync(db, id);
            Assert.Equal(true, columns.Explicit);
            Assert.Equal("llm", columns.ExplicitSource);
        }
    }
}
