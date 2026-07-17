// STORY-051 — Re-enrichment endpoints single + bulk (WIRE)
//
// BDD specification — xUnit. POST /api/media/{id}/reenrich + POST /api/media/bulk/reenrich.
// Each ReenrichFields flag maps to a specific sentinel-reset (F20.10); the existing enricher worker
// reclaims affected rows via its shipped backfill predicates. Operator-gated until L6 lands.
// See docs/PLAN.md Epic J.
//
// In-process tests (FeatureReenrichmentEndpointsInProcess): construct ReenrichController directly
// with a fake IAdminMediaReenrichment — no live stack required.
//
// Operator-gated integration scenarios (FeatureReenrichmentEndpoints): remain Skip-pinned.

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scriptable <see cref="IAdminMediaReenrichment"/>. Records the last call for assertion.
/// Defaults to <see cref="ReenrichResult.Scheduled"/> / 0 so tests that do not configure it
/// still get a well-defined result.
/// </summary>
file sealed class FakeAdminMediaReenrichment : IAdminMediaReenrichment
{
    public ReenrichResult ScheduleResult { get; set; } = ReenrichResult.Scheduled;
    public int ScheduleBulkResult        { get; set; }

    /// <summary>The fields value passed to the last <see cref="ScheduleAsync"/> call.</summary>
    public ReenrichFields? LastScheduleFields { get; private set; }

    public Task<ReenrichResult> ScheduleAsync(
        string id,
        ReenrichFields fields,
        LibraryScope scope,
        CancellationToken ct)
    {
        LastScheduleFields = fields;
        return Task.FromResult(ScheduleResult);
    }

    public Task<int> ScheduleBulkAsync(
        MediaQuery filter,
        ReenrichFields fields,
        LibraryScope scope,
        CancellationToken ct)
        => Task.FromResult(ScheduleBulkResult);
}

/// <summary>
/// Builds a <see cref="ReenrichController"/> wired to the given fake and returns both the
/// controller and the underlying <see cref="DefaultHttpContext"/> so tests can inspect
/// response headers and status codes after the action executes.
/// </summary>
file static class ReenrichControllerFactory
{
    public static (ReenrichController Controller, DefaultHttpContext HttpContext) Build(
        IAdminMediaReenrichment adminReenrichment,
        IStationScopeProvider scopeProvider)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.ContentType = "application/json";

        var controller = new ReenrichController(
            adminReenrichment,
            scopeProvider,
            NullLogger<ReenrichController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx },
        };

        return (controller, ctx);
    }

    /// <summary>Creates an <see cref="IStationScopeProvider"/> fixed to the given library scope.</summary>
    public static IStationScopeProvider ScopeWith(params long[] libraryIds) =>
        new FakeStationScopeProvider(new LibraryScope(libraryIds));
}

// ── In-process tests ──────────────────────────────────────────────────────────────────────────────

public static class FeatureReenrichmentEndpointsInProcess
{
    // ── Fields parse helper (ReenrichFieldsParser) ───────────────────────────────────────────────

    public sealed class ScenarioFieldsParseHelper
    {
        [Theory]
        [InlineData(null,    ReenrichFields.All)]
        [InlineData("",      ReenrichFields.All)]
        [InlineData("   ",   ReenrichFields.All)]
        [InlineData("all",   ReenrichFields.All)]
        [InlineData("ALL",   ReenrichFields.All)]
        public void NullEmptyOrAllTokenNormalizesToAll(string? input, ReenrichFields expected)
        {
            var ok = ReenrichFieldsParser.TryParse(input, out var result);
            Assert.True(ok);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void CueTokenReturnsCueFlag()
        {
            var ok = ReenrichFieldsParser.TryParse("cue", out var result);
            Assert.True(ok);
            Assert.Equal(ReenrichFields.Cue, result);
        }

        [Fact]
        public void EnergyTokenReturnsEnergyFlag()
        {
            var ok = ReenrichFieldsParser.TryParse("energy", out var result);
            Assert.True(ok);
            Assert.Equal(ReenrichFields.Energy, result);
        }

        [Fact]
        public void LoudnessTokenReturnsLoudnessFlag()
        {
            var ok = ReenrichFieldsParser.TryParse("loudness", out var result);
            Assert.True(ok);
            Assert.Equal(ReenrichFields.Loudness, result);
        }

        [Fact]
        public void TagsTokenReturnsTagsFlag()
        {
            var ok = ReenrichFieldsParser.TryParse("tags", out var result);
            Assert.True(ok);
            Assert.Equal(ReenrichFields.Tags, result);
        }

        [Fact]
        public void BpmTokenReturnsBpmFlag()
        {
            var ok = ReenrichFieldsParser.TryParse("bpm", out var result);
            Assert.True(ok);
            Assert.Equal(ReenrichFields.Bpm, result);
        }

        [Fact]
        public void YearTokenReturnsYearFlag()
        {
            var ok = ReenrichFieldsParser.TryParse("year", out var result);
            Assert.True(ok);
            Assert.Equal(ReenrichFields.Year, result);
        }

        [Fact]
        public void CueAndEnergyTokensReturnCombinedFlags()
        {
            // AC6: fields=cue,energy → only cue+energy flags; loudness/tags/state untouched.
            var ok = ReenrichFieldsParser.TryParse("cue,energy", out var result);
            Assert.True(ok);
            Assert.Equal(ReenrichFields.Cue | ReenrichFields.Energy, result);
        }

        [Fact]
        public void CaseInsensitiveTokensAreParsed()
        {
            var ok = ReenrichFieldsParser.TryParse("CUE,ENERGY", out var result);
            Assert.True(ok);
            Assert.Equal(ReenrichFields.Cue | ReenrichFields.Energy, result);
        }

        [Fact]
        public void UnknownTokenReturnsFalse()
        {
            // AC8: unknown field → 400, nothing written.
            var ok = ReenrichFieldsParser.TryParse("nonsense", out _);
            Assert.False(ok);
        }

        [Fact]
        public void MixedValidAndUnknownTokenReturnsFalse()
        {
            // AC8: even one bad token poisons the whole input.
            var ok = ReenrichFieldsParser.TryParse("cue,nonsense", out _);
            Assert.False(ok);
        }

        [Fact]
        public void NoneTokenReturnsFalse()
        {
            // "none" is a valid enum member but not a valid user-facing token.
            var ok = ReenrichFieldsParser.TryParse("none", out _);
            Assert.False(ok);
        }

        [Fact]
        public void EmptyTokenListNormalizesToAll()
        {
            // Array overload: null list → All.
            var ok = ReenrichFieldsParser.TryParse((IReadOnlyList<string>?)null, out var result);
            Assert.True(ok);
            Assert.Equal(ReenrichFields.All, result);
        }

        [Fact]
        public void EmptyArrayNormalizesToAll()
        {
            var ok = ReenrichFieldsParser.TryParse([], out var result);
            Assert.True(ok);
            Assert.Equal(ReenrichFields.All, result);
        }

        [Fact]
        public void ArrayWithCueAndEnergyReturnsCombinedFlags()
        {
            IReadOnlyList<string> tokens = ["cue", "energy"];
            var ok = ReenrichFieldsParser.TryParse(tokens, out var result);
            Assert.True(ok);
            Assert.Equal(ReenrichFields.Cue | ReenrichFields.Energy, result);
        }

        [Fact]
        public void ArrayWithUnknownTokenReturnsFalse()
        {
            IReadOnlyList<string> tokens = ["cue", "unknown"];
            var ok = ReenrichFieldsParser.TryParse(tokens, out _);
            Assert.False(ok);
        }

        [Fact]
        public void NumericStringFourReturnsFalse()
        {
            // "4" is the numeric value of Loudness; Enum.TryParse accepted it, but it is not a
            // documented token name — must be 400 (AC8).
            var ok = ReenrichFieldsParser.TryParse("4", out _);
            Assert.False(ok);
        }

        [Fact]
        public void NumericStringSixteenReturnsFalse()
        {
            // "16" is undefined in the enum; accepting it via TryParse caused a 500 from malformed SQL.
            var ok = ReenrichFieldsParser.TryParse("16", out _);
            Assert.False(ok);
        }

        [Fact]
        public void NumericStringZeroReturnsFalse()
        {
            // "0" maps to None by value but is not a valid user token → 400.
            var ok = ReenrichFieldsParser.TryParse("0", out _);
            Assert.False(ok);
        }

        [Fact]
        public void CsvWithNumericTokenReturnsFalse()
        {
            // One numeric token in a CSV poisons the whole input → 400.
            var ok = ReenrichFieldsParser.TryParse("cue,4", out _);
            Assert.False(ok);
        }

        [Fact]
        public void ArrayWithNumericTokenReturnsFalse()
        {
            // Array overload: one numeric token → 400.
            IReadOnlyList<string> tokens = ["loudness", "99"];
            var ok = ReenrichFieldsParser.TryParse(tokens, out _);
            Assert.False(ok);
        }
    }

    // ── Single-row endpoint: status code mapping ─────────────────────────────────────────────────

    public sealed class ScenarioSingleRowStatusCodes
    {
        [Fact]
        public async Task ScheduledResultMaps202()
        {
            // AC1-AC4: successful re-enrichment schedule → 202 Accepted (body-less).
            var fake = new FakeAdminMediaReenrichment { ScheduleResult = ReenrichResult.Scheduled };
            var (controller, _) = ReenrichControllerFactory.Build(fake, ReenrichControllerFactory.ScopeWith(1));

            var result = await controller.Reenrich(99L, fields: null, CancellationToken.None);

            var status = Assert.IsType<StatusCodeResult>(result);
            Assert.Equal(202, status.StatusCode);
        }

        [Fact]
        public async Task NotFoundResultMaps404()
        {
            // AC9: unknown id → 404.
            var fake = new FakeAdminMediaReenrichment { ScheduleResult = ReenrichResult.NotFound };
            var (controller, _) = ReenrichControllerFactory.Build(fake, ReenrichControllerFactory.ScopeWith(1));

            var result = await controller.Reenrich(9999L, fields: "cue", CancellationToken.None);

            Assert.IsType<NotFoundResult>(result);
        }

        // AC10 SUPERSEDED by SPEC F43.3 (Epic V, closes gitea-#203): the row-outside-scope 403 is
        // repealed — ReenrichResult.OutOfScope no longer exists (fully unused once the single-row
        // scope gate was removed, so it was deleted rather than kept as a dead member). A row
        // outside station scope now schedules exactly like any other row.
        [Fact]
        public async Task ScheduledResultMaps202RegardlessOfTheCallersStationScope()
        {
            var fake = new FakeAdminMediaReenrichment { ScheduleResult = ReenrichResult.Scheduled };
            // Empty scope — the strictest possible caller under the old regime — still 202s.
            var (controller, _) = ReenrichControllerFactory.Build(fake, ReenrichControllerFactory.ScopeWith());

            var result = await controller.Reenrich(42L, fields: "energy", CancellationToken.None);

            var status = Assert.IsType<StatusCodeResult>(result);
            Assert.Equal(202, status.StatusCode);
        }

        [Fact]
        public async Task UnknownFieldTokenReturns400BeforeCallingRepo()
        {
            // AC8: unknown fields token → 400; fake should not be called.
            var fake = new FakeAdminMediaReenrichment();
            var (controller, _) = ReenrichControllerFactory.Build(fake, ReenrichControllerFactory.ScopeWith(1));

            var result = await controller.Reenrich(99L, fields: "cue,nonsense", CancellationToken.None);

            var bad = Assert.IsType<BadRequestObjectResult>(result);
            Assert.IsType<ProblemDetails>(bad.Value);
            // Repo was NOT called — LastScheduleFields remains null.
            Assert.Null(fake.LastScheduleFields);
        }

        [Fact]
        public async Task EmptyFieldsParamNormalizesToAllAndReturns202()
        {
            // AC5: missing fields param → All; 202.
            var fake = new FakeAdminMediaReenrichment { ScheduleResult = ReenrichResult.Scheduled };
            var (controller, _) = ReenrichControllerFactory.Build(fake, ReenrichControllerFactory.ScopeWith(1));

            await controller.Reenrich(99L, fields: null, CancellationToken.None);

            Assert.Equal(ReenrichFields.All, fake.LastScheduleFields);
        }

        [Fact]
        public async Task CueEnergyFieldsPassCorrectFlagsToRepo()
        {
            // AC6: fields=cue,energy → only cue+energy flags forwarded to repo.
            var fake = new FakeAdminMediaReenrichment { ScheduleResult = ReenrichResult.Scheduled };
            var (controller, _) = ReenrichControllerFactory.Build(fake, ReenrichControllerFactory.ScopeWith(1));

            await controller.Reenrich(99L, fields: "cue,energy", CancellationToken.None);

            Assert.Equal(ReenrichFields.Cue | ReenrichFields.Energy, fake.LastScheduleFields);
        }
    }

    // ── Bulk endpoint: status code + response body ───────────────────────────────────────────────

    public sealed class ScenarioBulkReenrichResponse
    {
        [Fact]
        public async Task ReturnsOkWithScheduledCount()
        {
            // AC7: 200 { scheduled: N }.
            var fake = new FakeAdminMediaReenrichment { ScheduleBulkResult = 5 };
            var (controller, _) = ReenrichControllerFactory.Build(fake, ReenrichControllerFactory.ScopeWith(1));
            var request = new BulkReenrichRequest(Filter: null, Fields: null);

            var result = await controller.BulkReenrich(request, CancellationToken.None);

            var ok   = Assert.IsType<OkObjectResult>(result);
            var json = JsonSerializer.Serialize(ok.Value);
            var doc  = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("scheduled", out var prop));
            Assert.Equal(5, prop.GetInt32());
        }

        [Fact]
        public async Task ZeroScheduledCountIsAlsoOk()
        {
            // Empty scope or filter matches nothing → 200 { scheduled: 0 }.
            var fake = new FakeAdminMediaReenrichment { ScheduleBulkResult = 0 };
            var (controller, _) = ReenrichControllerFactory.Build(fake, ReenrichControllerFactory.ScopeWith(1));
            var request = new BulkReenrichRequest(Filter: null, Fields: ["cue", "energy"]);

            var result = await controller.BulkReenrich(request, CancellationToken.None);

            var ok   = Assert.IsType<OkObjectResult>(result);
            var json = JsonSerializer.Serialize(ok.Value);
            var doc  = JsonDocument.Parse(json);
            Assert.Equal(0, doc.RootElement.GetProperty("scheduled").GetInt32());
        }

        [Fact]
        public async Task UnknownFieldInArrayReturns400()
        {
            // AC8: unknown token in bulk fields array → 400, nothing written.
            var fake = new FakeAdminMediaReenrichment();
            var (controller, _) = ReenrichControllerFactory.Build(fake, ReenrichControllerFactory.ScopeWith(1));
            var request = new BulkReenrichRequest(Filter: null, Fields: ["cue", "bad_field"]);

            var result = await controller.BulkReenrich(request, CancellationToken.None);

            var bad = Assert.IsType<BadRequestObjectResult>(result);
            Assert.IsType<ProblemDetails>(bad.Value);
        }

        [Fact]
        public async Task NullFieldsArrayNormalizesToAllAndReturns200()
        {
            // AC5: absent fields array → All.
            var fake = new FakeAdminMediaReenrichment { ScheduleBulkResult = 3 };
            var (controller, _) = ReenrichControllerFactory.Build(fake, ReenrichControllerFactory.ScopeWith(1));
            var request = new BulkReenrichRequest(Filter: null, Fields: null);

            var result = await controller.BulkReenrich(request, CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task EmptyFieldsArrayNormalizesToAllAndReturns200()
        {
            // AC5: empty fields array → All.
            var fake = new FakeAdminMediaReenrichment { ScheduleBulkResult = 2 };
            var (controller, _) = ReenrichControllerFactory.Build(fake, ReenrichControllerFactory.ScopeWith(1));
            var request = new BulkReenrichRequest(Filter: null, Fields: []);

            var result = await controller.BulkReenrich(request, CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
        }
    }
}

// ── Operator-gated (live stack) ───────────────────────────────────────────────────────────────────

public static class FeatureReenrichmentEndpoints
{
    const string Pending = "Pending L6 — re-enrichment endpoints; operator-gated live, see docs/PLAN.md Epic J";

    // ---------------------------------------------------------------------
    // HAPPY PATH — single-row, per-field semantics (F20.10)
    // ---------------------------------------------------------------------

    public sealed class ScenarioFieldsEqualsCueClearsCueSentinelsRowStaysReady
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void Returns202AndNullsTheCueColumnsAndAnalyzedAt()
        {
            // POST /api/media/{id}/reenrich?fields=cue → 202; cue_in_sec, cue_out_sec, cue_analyzed_at all NULL;
            // state remains 'ready'.
            Assert.Fail("pending L6");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void ExistingF13BackfillPredicateReclaimsTheRowOnTheNextEnricherTick()
        {
            // The shipped enricher's claim predicate (state='ready' AND cue_analyzed_at IS NULL) reclaims
            // the row; within ≤5 ticks the cue columns are repopulated.
            Assert.Fail("pending L6");
        }
    }

    public sealed class ScenarioFieldsEqualsEnergyClearsEnergySentinelsRowStaysReady
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void Returns202AndNullsTheEnergyColumnsAndAnalyzedAt()
        {
            // POST .../reenrich?fields=energy → 202; intro_energy, outro_energy, energy_analyzed_at all NULL;
            // state stays 'ready'.
            Assert.Fail("pending L6");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void ExistingF17BackfillPredicateReclaimsTheRowOnTheNextEnricherTick()
        {
            // The shipped enricher's claim predicate (state='ready' AND energy_analyzed_at IS NULL) reclaims
            // the row; within ≤5 ticks the energy columns are repopulated.
            Assert.Fail("pending L6");
        }
    }

    public sealed class ScenarioFieldsEqualsLoudnessDropsStateRowLeavesRotation
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void Returns202AndNullsLoudnessColumnsAndSetsStateToDiscovered()
        {
            // POST .../reenrich?fields=loudness → 202; integrated_lufs, true_peak_dbtp, measurable NULL;
            // state = 'discovered'.
            Assert.Fail("pending L6");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void GetRandomReadyAsyncNoLongerReturnsTheRowUntilReenrichmentCompletes()
        {
            // Until the enricher completes the full discovered → ready pipeline, the row is unselectable.
            Assert.Fail("pending L6");
        }
    }

    public sealed class ScenarioFieldsEqualsTagsClearsFreezeSentinelAndDropsState
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void Returns202AndNullsTagsEditedAtAndSetsStateToDiscovered()
        {
            // POST .../reenrich?fields=tags → 202; tags_edited_at = NULL; state = 'discovered'.
            // After the enricher re-processes the row, the file's embedded tags overwrite operator-edited
            // tag columns (explicit escape hatch).
            Assert.Fail("pending L6");
        }
    }

    public sealed class ScenarioMultiFieldAndDefaultAllApplyAtomically
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void NoFieldsQueryDefaultsToAllAndAppliesAllFourResets()
        {
            // POST .../reenrich (no fields query) applies cue + energy + loudness + tags resets in one
            // transaction. Equivalent to fields=all.
            Assert.Fail("pending L6");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void MultiFieldQueryAppliesEachListedResetAndLeavesOthersUntouched()
        {
            // POST .../reenrich?fields=cue,energy nulls cue + energy sentinels only; loudness columns,
            // state, and tags_edited_at are unchanged.
            Assert.Fail("pending L6");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — bulk
    // ---------------------------------------------------------------------

    public sealed class ScenarioBulkReenrichSchedulesAcrossTheFilterSet
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void PostWithFilterAndFieldsReturnsScheduledCount()
        {
            // POST /api/media/bulk/reenrich { "filter": { "artist": "Brian Eno" }, "fields": ["cue", "energy"] }
            // → 200 with body { scheduled: <count> }. Every matching in-scope row's cue+energy sentinels are NULL.
            Assert.Fail("pending L6");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void EnricherConsumesTheBackfillQueueAtItsExistingFiftyPerTickCap()
        {
            // The endpoint is a sentinel reset, not synchronous analysis. The shipped 50/tick cap throttles
            // the resulting backfill regardless of the scheduled count.
            Assert.Fail("pending L6");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioValidationAndAuth
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void UnknownFieldNameReturns400()
        {
            // POST .../reenrich?fields=cue,nonsense → 400 ProblemDetails; nothing is written.
            Assert.Fail("pending L6");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void UnknownIdOnSingleRowEndpointReturns404()
        {
            // POST /api/media/9999/reenrich on a non-existent id → 404 ProblemDetails.
            Assert.Fail("pending L6");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void OutOfScopeRowOnSingleRowEndpointStillReturns202()
        {
            // SUPERSEDED by SPEC F43.3 (Epic V, closes gitea-#203): POST /api/media/{id}/reenrich on a
            // row whose library is not in Station:Scope:LibraryIds no longer 403s — the old
            // source-row scope gate is repealed and the row schedules like any other.
            Assert.Fail("pending L6");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void BulkFilterIsBoundedByStationScope()
        {
            // Station:Scope:LibraryIds = [1]; matching rows exist in libraries 1 and 3. The bulk reenrich
            // affects only rows in library 1.
            Assert.Fail("pending L6");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void BulkWriteWithoutCookieOrWithNonJsonContentTypeIsRejected()
        {
            // With Admin:Password set: no cookie → 401; non-JSON Content-Type → 415.
            Assert.Fail("pending L6");
        }
    }

    public sealed class ScenarioBulkReenrichStormNeverProducesSilence
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void RemainingReadyRowsContinueToFeedRotationDuringConvergence()
        {
            // A bulk reenrich on fields=loudness against thousands of rows sets them to state='discovered';
            // remaining ready rows feed the rotation throughout. If every selectable row is briefly ineligible,
            // the F4 safe-rotation backstop engages — never silence.
            Assert.Fail("pending L6");
        }
    }
}
