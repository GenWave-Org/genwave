// STORY-392 — I manage the Ads library (API half · F162.1 · PLAN T403)
// Also carries STORY-390 AC9 (the owner's editor gets the same law — validator at save).
// The page half (AC1–AC5 in a browser) is specced in admin-ui/__specs__/ads-page.spec.tsx.
//
// BDD specification — xUnit through the deployed entry point (WebApplicationFactory<Program> against
// a real ephemeral Postgres — the Story374/Story382 arc idiom): every fact drives GET/POST/PATCH
// /api/ads* over HTTP with an authed admin session, never AdSpotRepository/AdsController directly.
// One arc (AdsApiArc) arranges everything every HAPPY-PATH/validator/If-Match/PATCH Scenario below
// reads (the SAME "arrange once, many read-only Scenarios" idiom Story374's
// GardenerFindingsCollection/Story382's KindScopedPagingCollection already establish); the
// admin-surface posture Scenario needs no real database at all (SurfaceGateMiddleware 404s before any
// store is ever touched), so it gets its own, DB-less factory (the Story166 KillSwitchWebFactory /
// Story374 GardenerSurfaceWebFactory precedent).
//
// T403 review round 2 (finding 1): the If-Match-malformed fact was verified against a MUTANT — the
// controller's own uint.TryParse guard temporarily removed — to confirm it goes red without the guard
// (never a vacuous pass); the guard stays in AdsController, this is a process note, not a permanent
// test artifact.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAdsApi
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — through the production surface (WebApplicationFactory)
    // ---------------------------------------------------------------------

    [Collection(AdsApiCollection.Name)]
    public sealed class ScenarioTheEditorRoundTrips(AdsApiArc arc)
    {
        [Fact]
        public void AValidDraftPostsAndReadsBackEveryField()
        {
            // POST /api/ads {brand,title,script,voices,seconds,bed} → GET returns it verbatim.
            Assert.Equal(HttpStatusCode.Created, arc.CleanDraftPostStatus);
            Assert.Equal(HttpStatusCode.OK, arc.RoundTripGetStatus);
            Assert.Equal(AdsApiArc.CleanDraftBrand, arc.RoundTripBrand);
            Assert.Equal(AdsApiArc.CleanDraftTitle, arc.RoundTripTitle);
            Assert.Equal(AdsApiArc.CleanDraftScript, arc.RoundTripScript);
            Assert.Equal(30, arc.RoundTripSpotSeconds);
            Assert.Equal(arc.BedMediaId, arc.RoundTripBedMediaId);
            Assert.Equal("draft", arc.RoundTripState);
            Assert.Equal("ANNOUNCER", arc.RoundTripVoicePlanFirstTag);
        }

        [Fact]
        public void AVerbatimQuirkyScriptSurvivesByteForByte()
        {
            // STORY-390 AC9/F160.4's "no LLM touches it": a script carrying formatting a normalizer
            // would plausibly reshape (irregular internal whitespace, an em dash, mixed capitalization)
            // — a DIFFERENT script, a DIFFERENT draft, a DIFFERENT dimension than the breadth check
            // above (which posts one clean script and checks every OTHER field): this one is entirely
            // about whether the text itself survives untouched.
            Assert.Equal(HttpStatusCode.Created, arc.VerbatimQuirkyPostStatus);
            Assert.Equal(AdsApiArc.VerbatimQuirkyScript, arc.VerbatimQuirkyRoundTripScript);
        }
    }

    [Collection(AdsApiCollection.Name)]
    public sealed class ScenarioVerbsDriveTheStateMachine(AdsApiArc arc)
    {
        [Fact]
        public void ApproveMovesADraftToApproved()
        {
            // Also the "approve-happy-still-works" pin (T403 review finding 5): this draft carries a
            // clean, valid script, so the NEW validate-current-script gate Approve now runs must let
            // it through exactly as before.
            Assert.Equal(HttpStatusCode.OK, arc.ApproveStatus);
            Assert.Equal("approved", arc.ApproveResultState);
        }

        [Fact]
        public void RetryMovesAFailedSpotToApproved()
        {
            // Same "gate doesn't block a valid script" pin, one verb over.
            Assert.Equal(HttpStatusCode.OK, arc.RetryStatus);
            Assert.Equal("approved", arc.RetryResultState);
        }

        [Fact]
        public void RetireMovesAReadySpotToRetired()
        {
            Assert.Equal(HttpStatusCode.OK, arc.RetireStatus);
            Assert.Equal("retired", arc.RetireResultState);
        }

        [Fact]
        public void TheListPagesByStateOnTheSharedShape()
        {
            // GET /api/ads?state=draft&limit=2&offset=0 — the Gardener/T385 paging idiom (exact
            // total, one round trip): 6 draft rows exist by the time this arc's own arrangement
            // finishes (the clean round-trip draft, the verbatim-quirk draft, the brief-only draft
            // whose approve was refused — still draft — plus 3 seeded purely for this fact); a page
            // of 2 carries an exact total of 6, never derived from the page's own row count.
            Assert.Equal(HttpStatusCode.OK, arc.DraftListStatus);
            Assert.Equal(2, arc.DraftListItemCount);
            Assert.Equal(6, arc.DraftListTotal);
            Assert.All(arc.DraftListStates, state => Assert.Equal("draft", state));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the validator guards every save
    // ---------------------------------------------------------------------

    [Collection(AdsApiCollection.Name)]
    public sealed class ScenarioTheValidatorGuardsTheSave(AdsApiArc arc)
    {
        [Fact]
        public void AViolatingScriptIs400WithTheRuleId()
        {
            // STORY-390 AC9: a blocklisted brand in an owner script → 400 naming brand-collision.
            Assert.Equal(HttpStatusCode.BadRequest, arc.ViolatingPostStatus);
            Assert.Equal("brand_collision", arc.ViolatingPostRuleId);
            Assert.Equal("script", arc.ViolatingPostField);
        }

        [Fact]
        public void MalformedVoicePlanIs400()
        {
            // A blank voicePlan[].tag is refused at save, never silently dropped (PLAN T403's own
            // ruling — see AdsController.ValidateVoicePlanEntries's own remarks).
            Assert.Equal(HttpStatusCode.BadRequest, arc.MalformedVoicePlanPostStatus);
        }

        [Fact]
        public void UnknownBedIs400()
        {
            // bedMediaId is resolved to a real row before it is ever stored (SafeSegmentsController's
            // own precedent) — an unknown id refuses the whole save.
            Assert.Equal(HttpStatusCode.BadRequest, arc.UnknownBedPostStatus);
        }

        [Fact]
        public void SpotSecondsOutsideAllowedSetIs400()
        {
            // spotSeconds must be one of the three shipped structures — 45 is not one of them.
            Assert.Equal(HttpStatusCode.BadRequest, arc.SpotSecondsOutOfRangePostStatus);
        }
    }

    [Collection(AdsApiCollection.Name)]
    public sealed class ScenarioListValidation(AdsApiArc arc)
    {
        [Fact]
        public void UnknownStateQueryIs400()
        {
            // ?state=not_a_real_state names the field and the allowed set, never 500s.
            Assert.Equal(HttpStatusCode.BadRequest, arc.UnknownStateQueryStatus);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — If-Match validated before it ever reaches SQL (T403 carry-forward b)
    // ---------------------------------------------------------------------

    [Collection(AdsApiCollection.Name)]
    public sealed class ScenarioIfMatchIsValidatedBeforeSql(AdsApiArc arc)
    {
        [Fact]
        public void AnAbsentIfMatchIs428()
        {
            Assert.Equal(HttpStatusCode.PreconditionRequired, arc.IfMatchAbsentStatus);
        }

        [Fact]
        public void AMalformedIfMatchIs400()
        {
            // Never a raw PostgresException 22P02 — the exact carry-forward this task closes.
            Assert.Equal(HttpStatusCode.BadRequest, arc.IfMatchMalformedStatus);
        }

        [Fact]
        public void AStaleIfMatchIs409OverHttp()
        {
            // The SAME token that succeeded once is stale on the second attempt — the store's own
            // Conflict outcome, over the wire.
            Assert.Equal(HttpStatusCode.Conflict, arc.StaleIfMatchStatus);
        }
    }

    // ---------------------------------------------------------------------
    // The PATCH editor — sparse edit, verbatim elsewhere, state-guarded
    // ---------------------------------------------------------------------

    [Collection(AdsApiCollection.Name)]
    public sealed class ScenarioThePatchEditor(AdsApiArc arc)
    {
        [Fact]
        public void APatchChangesOnlyTheGivenFieldAndReturnsAFreshETag()
        {
            Assert.Equal(HttpStatusCode.OK, arc.PatchStatus);
            Assert.Equal(AdsApiArc.PatchedTitle, arc.PatchedRoundTripTitle);
            Assert.NotEqual(arc.CleanDraftEtag, arc.PatchedEtag);
        }

        [Fact]
        public void PatchLeavesEveryOtherFieldVerbatim()
        {
            // Sparse: only title was sent. Brand, script, and spotSeconds — none of which rode this
            // PATCH body — must read back exactly as the original POST left them.
            Assert.Equal(AdsApiArc.CleanDraftBrand, arc.PatchedRoundTripBrand);
            Assert.Equal(AdsApiArc.CleanDraftScript, arc.PatchedRoundTripScript);
            Assert.Equal(30, arc.PatchedRoundTripSpotSeconds);
        }

        [Fact]
        public void PatchOnAnApprovedSpotIs409()
        {
            // PLAN T403's own ruling: editing an approved spot would invalidate a render already
            // claimed or already landed.
            Assert.Equal(HttpStatusCode.Conflict, arc.PatchOnApprovedStatus);
        }
    }

    // ---------------------------------------------------------------------
    // Approve/Retry gate on the CURRENT script (T403 review RULING, finding 5)
    // ---------------------------------------------------------------------

    [Collection(AdsApiCollection.Name)]
    public sealed class ScenarioApproveAndRetryGateOnTheCurrentScript(AdsApiArc arc)
    {
        [Fact]
        public void RetryRefusesAStillInvalidScript()
        {
            // A failed spot whose (unedited) script still names a blocklisted brand — retry refuses
            // it with the rule id, never reaching a doomed render cycle three tasks downstream.
            Assert.Equal(HttpStatusCode.BadRequest, arc.RetryInvalidScriptStatus);
            Assert.Equal("brand_collision", arc.RetryInvalidScriptRuleId);
        }

        [Fact]
        public void ApproveRefusesABriefOnlyDraft()
        {
            // A brief is descriptive only — never itself validated, never airable. A null script
            // folds into the SAME format-rule refusal an empty one would hit.
            Assert.Equal(HttpStatusCode.BadRequest, arc.ApproveBriefOnlyStatus);
            Assert.Equal("format", arc.ApproveBriefOnlyRuleId);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the admin surface gates the queue
    // ---------------------------------------------------------------------

    public sealed class ScenarioAdminSurfacePosture
    {
        [Fact]
        public async Task EveryAdsRouteIs404WhileAdminIsDisabled()
        {
            // Admin:Enabled=false: /api/ads* 404s like every admin route (F162.1). No real database
            // needed — SurfaceGateMiddleware refuses before any store is ever touched (the
            // Story166/Story374 DB-less-factory precedent).
            await using var factory = new AdsAdminOffWebFactory();
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var routes = new (HttpMethod Method, string Path, HttpContent? Body)[]
            {
                (HttpMethod.Get, "/api/ads", null),
                (HttpMethod.Get, "/api/ads/1", null),
                (HttpMethod.Post, "/api/ads", JsonContent.Create(new { })),
                (HttpMethod.Patch, "/api/ads/1", JsonContent.Create(new { })),
                (HttpMethod.Post, "/api/ads/1/approve", null),
                (HttpMethod.Post, "/api/ads/1/retry", null),
                (HttpMethod.Post, "/api/ads/1/retire", null),
            };

            foreach (var (method, path, body) in routes)
            {
                var request = new HttpRequestMessage(method, path) { Content = body };
                var response = await client.SendAsync(request);
                Assert.True(
                    response.StatusCode == HttpStatusCode.NotFound,
                    $"{method} {path} returned {(int)response.StatusCode} with Admin:Enabled=false.");
            }
        }
    }
}

// ── Collection definition — one ephemeral Postgres/factory shared by every happy-path/validator/
// If-Match/PATCH Scenario above (the Story374/Story382 "arrange once, many read-only Scenarios"
// idiom). ──

[CollectionDefinition(Name)]
public sealed class AdsApiCollection : ICollectionFixture<AdsApiArc>
{
    public const string Name = "Story392AdsApi";
}

/// <summary>
/// Arranges every fact STORY-392's API-half Scenarios read, entirely over the REAL production HTTP
/// pipeline with a real admin session — no <c>AdSpotRepository</c> call, no <c>AdsController</c> call,
/// anywhere in this class. Some rows are seeded directly via raw SQL, bypassing the API entirely (the
/// <c>GardenerRotFixtures</c> precedent): a <c>failed</c> spot (retry needs a row the create endpoint
/// itself can never produce — POST always births <c>draft</c>) and a <c>ready</c> spot (retire's own
/// precondition; a ready spot only exists past the render worker, deliberately not run in this
/// factory).
/// </summary>
public sealed class AdsApiArc : IAsyncLifetime
{
    public const string CleanDraftBrand = "Cravin's Diner";
    public const string CleanDraftTitle = "Diner spring special";
    public const string CleanDraftScript =
        "ANNOUNCER: Cravin's Diner has a deal so good it's almost illegal.\nANNOUNCER: Call 555-0100 today.";

    public const string VerbatimQuirkyScript =
        "ANNOUNCER: BIG   savings — don't  miss THIS one.\nANNOUNCER: Call 555-0177 today.";

    public const string PatchedTitle = "Diner spring special (v2)";

    const string OtherwiseValidScript =
        "ANNOUNCER: A perfectly ordinary announcement.\nANNOUNCER: Call 555-0199 today.";

    const string InvalidBrandCollisionScript =
        "ANNOUNCER: Nothing beats an ice cold Coca Cola on a hot day.\nANNOUNCER: Call 555-0100 today.";

    public long BedMediaId { get; private set; }

    public HttpStatusCode CleanDraftPostStatus { get; private set; }
    public long CleanDraftId { get; private set; }
    public string CleanDraftEtag { get; private set; } = "";

    public HttpStatusCode RoundTripGetStatus { get; private set; }
    public string RoundTripBrand { get; private set; } = "";
    public string RoundTripTitle { get; private set; } = "";
    public string? RoundTripScript { get; private set; }
    public int RoundTripSpotSeconds { get; private set; }
    public long? RoundTripBedMediaId { get; private set; }
    public string RoundTripState { get; private set; } = "";
    public string? RoundTripVoicePlanFirstTag { get; private set; }

    public HttpStatusCode VerbatimQuirkyPostStatus { get; private set; }
    public string? VerbatimQuirkyRoundTripScript { get; private set; }

    public HttpStatusCode PatchStatus { get; private set; }
    public string PatchedEtag { get; private set; } = "";
    public string PatchedRoundTripTitle { get; private set; } = "";
    public string PatchedRoundTripBrand { get; private set; } = "";
    public string? PatchedRoundTripScript { get; private set; }
    public int PatchedRoundTripSpotSeconds { get; private set; }

    public HttpStatusCode PatchOnApprovedStatus { get; private set; }

    public HttpStatusCode ViolatingPostStatus { get; private set; }
    public string? ViolatingPostRuleId { get; private set; }
    public string? ViolatingPostField { get; private set; }

    public HttpStatusCode MalformedVoicePlanPostStatus { get; private set; }
    public HttpStatusCode UnknownBedPostStatus { get; private set; }
    public HttpStatusCode SpotSecondsOutOfRangePostStatus { get; private set; }
    public HttpStatusCode UnknownStateQueryStatus { get; private set; }

    public HttpStatusCode IfMatchAbsentStatus { get; private set; }
    public HttpStatusCode IfMatchMalformedStatus { get; private set; }
    public HttpStatusCode StaleIfMatchStatus { get; private set; }

    public HttpStatusCode ApproveStatus { get; private set; }
    public string? ApproveResultState { get; private set; }

    public HttpStatusCode RetryStatus { get; private set; }
    public string? RetryResultState { get; private set; }

    public HttpStatusCode RetryInvalidScriptStatus { get; private set; }
    public string? RetryInvalidScriptRuleId { get; private set; }

    public HttpStatusCode ApproveBriefOnlyStatus { get; private set; }
    public string? ApproveBriefOnlyRuleId { get; private set; }

    public HttpStatusCode RetireStatus { get; private set; }
    public string? RetireResultState { get; private set; }

    public HttpStatusCode DraftListStatus { get; private set; }
    public int DraftListItemCount { get; private set; }
    public int DraftListTotal { get; private set; }
    public IReadOnlyList<string> DraftListStates { get; private set; } = [];

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field — Story392AdsDatabase is file-local (CS9051), the identical reason
        // Story374's/Story382's own arcs give for the same shape.
        await using var database = await Story392AdsDatabase.StartAsync();

        var bedId = await AdsWireFixtures.InsertPlayableMediaRowAsync(
            database.LibraryConnectionString, "/test/t403-bed.flac", "Bed Track", "Bed Artist");
        BedMediaId = bedId;

        await using var factory = new Story392AdsWebFactory(database);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = Story392AdsWebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        // ── The clean draft: the editor round-trip + verbatim-text facts. ──
        var createPayload = new
        {
            brand = CleanDraftBrand,
            title = CleanDraftTitle,
            script = CleanDraftScript,
            spotSeconds = 30,
            bedMediaId = bedId,
            voicePlan = new[] { new { tag = "ANNOUNCER", voiceId = "af_heart", pace = 1.0 } },
        };
        var createResponse = await client.PostAsJsonAsync("/api/ads", createPayload);
        CleanDraftPostStatus = createResponse.StatusCode;
        var created = await JsonDocument.ParseAsync(await createResponse.Content.ReadAsStreamAsync());
        CleanDraftId = created.RootElement.GetProperty("id").GetInt64();
        CleanDraftEtag = createResponse.Headers.ETag?.Tag ?? "";

        var getResponse = await client.GetAsync($"/api/ads/{CleanDraftId}");
        RoundTripGetStatus = getResponse.StatusCode;
        var round = await JsonDocument.ParseAsync(await getResponse.Content.ReadAsStreamAsync());
        RoundTripBrand = round.RootElement.GetProperty("brand").GetString() ?? "";
        RoundTripTitle = round.RootElement.GetProperty("title").GetString() ?? "";
        RoundTripScript = round.RootElement.GetProperty("script").GetString();
        RoundTripSpotSeconds = round.RootElement.GetProperty("spotSeconds").GetInt32();
        RoundTripBedMediaId = round.RootElement.GetProperty("bedMediaId").ValueKind == JsonValueKind.Null
            ? null : round.RootElement.GetProperty("bedMediaId").GetInt64();
        RoundTripState = round.RootElement.GetProperty("state").GetString() ?? "";
        var voicePlan = round.RootElement.GetProperty("voicePlan");
        RoundTripVoicePlanFirstTag = voicePlan.ValueKind == JsonValueKind.Array && voicePlan.GetArrayLength() > 0
            ? voicePlan[0].GetProperty("tag").GetString()
            : null;

        // ── The verbatim-quirk draft: a SEPARATE script, chosen specifically for formatting a
        // normalizer would plausibly reshape — T403 review finding 7's own distinctness demand. ──
        var quirkyCreateResponse = await client.PostAsJsonAsync("/api/ads", new
        {
            brand = "Widget Bros",
            title = "Widget spot",
            script = VerbatimQuirkyScript,
            spotSeconds = 30,
        });
        VerbatimQuirkyPostStatus = quirkyCreateResponse.StatusCode;
        var quirkyCreated = await JsonDocument.ParseAsync(await quirkyCreateResponse.Content.ReadAsStreamAsync());
        var quirkyId = quirkyCreated.RootElement.GetProperty("id").GetInt64();
        var quirkyGet = await client.GetAsync($"/api/ads/{quirkyId}");
        var quirkyRound = await JsonDocument.ParseAsync(await quirkyGet.Content.ReadAsStreamAsync());
        VerbatimQuirkyRoundTripScript = quirkyRound.RootElement.GetProperty("script").GetString();

        // ── The PATCH editor: a sparse edit — title only — leaving every other field verbatim, with
        // a fresh ETag. ──
        var patchRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/ads/{CleanDraftId}")
        {
            Content = JsonContent.Create(new { title = PatchedTitle }),
        };
        patchRequest.Headers.TryAddWithoutValidation("If-Match", CleanDraftEtag);
        var patchResponse = await client.SendAsync(patchRequest);
        PatchStatus = patchResponse.StatusCode;
        PatchedEtag = patchResponse.Headers.ETag?.Tag ?? "";

        var patchedGet = await client.GetAsync($"/api/ads/{CleanDraftId}");
        var patchedRound = await JsonDocument.ParseAsync(await patchedGet.Content.ReadAsStreamAsync());
        PatchedRoundTripTitle = patchedRound.RootElement.GetProperty("title").GetString() ?? "";
        PatchedRoundTripBrand = patchedRound.RootElement.GetProperty("brand").GetString() ?? "";
        PatchedRoundTripScript = patchedRound.RootElement.GetProperty("script").GetString();
        PatchedRoundTripSpotSeconds = patchedRound.RootElement.GetProperty("spotSeconds").GetInt32();

        // ── The validator guards every save: a blocklisted brand, a malformed voice plan, an unknown
        // bed, and an out-of-range spotSeconds each refuse whole. ──
        var violatingResponse = await client.PostAsJsonAsync("/api/ads", new
        {
            brand = "Fizzy Co",
            title = "Bad spot",
            script = InvalidBrandCollisionScript,
            spotSeconds = 30,
        });
        ViolatingPostStatus = violatingResponse.StatusCode;
        var violatingBody = await JsonDocument.ParseAsync(await violatingResponse.Content.ReadAsStreamAsync());
        ViolatingPostRuleId = violatingBody.RootElement.TryGetProperty("ruleId", out var ruleIdProperty)
            ? ruleIdProperty.GetString() : null;
        ViolatingPostField = violatingBody.RootElement.TryGetProperty("field", out var fieldProperty)
            ? fieldProperty.GetString() : null;

        var malformedVoicePlanResponse = await client.PostAsJsonAsync("/api/ads", new
        {
            brand = "Malformed Voice Plan Brand",
            title = "Malformed voice plan spot",
            script = OtherwiseValidScript,
            spotSeconds = 30,
            voicePlan = new[] { new { tag = "", voiceId = "af_heart", pace = 1.0 } },
        });
        MalformedVoicePlanPostStatus = malformedVoicePlanResponse.StatusCode;

        var unknownBedResponse = await client.PostAsJsonAsync("/api/ads", new
        {
            brand = "Unknown Bed Brand",
            title = "Unknown bed spot",
            script = OtherwiseValidScript,
            spotSeconds = 30,
            bedMediaId = 999_999_999,
        });
        UnknownBedPostStatus = unknownBedResponse.StatusCode;

        var spotSecondsOutOfRangeResponse = await client.PostAsJsonAsync("/api/ads", new
        {
            brand = "Out Of Range Brand",
            title = "Out of range spot",
            script = OtherwiseValidScript,
            spotSeconds = 45,
        });
        SpotSecondsOutOfRangePostStatus = spotSecondsOutOfRangeResponse.StatusCode;

        var unknownStateResponse = await client.GetAsync("/api/ads?state=not_a_real_state");
        UnknownStateQueryStatus = unknownStateResponse.StatusCode;

        // ── Approve: a second clean draft, created then approved (also proves the new
        // validate-current-script gate lets a valid script through). ──
        var approveDraftResponse = await client.PostAsJsonAsync("/api/ads", new
        {
            brand = "Approve Test Brand",
            title = "Approve test spot",
            script = OtherwiseValidScript,
            spotSeconds = 30,
        });
        var approveDraft = await JsonDocument.ParseAsync(await approveDraftResponse.Content.ReadAsStreamAsync());
        var approveDraftId = approveDraft.RootElement.GetProperty("id").GetInt64();
        var approveDraftEtag = approveDraftResponse.Headers.ETag?.Tag ?? "";

        var approveRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/ads/{approveDraftId}/approve");
        approveRequest.Headers.TryAddWithoutValidation("If-Match", approveDraftEtag);
        var approveResponse = await client.SendAsync(approveRequest);
        ApproveStatus = approveResponse.StatusCode;
        var approveBody = await JsonDocument.ParseAsync(await approveResponse.Content.ReadAsStreamAsync());
        ApproveResultState = approveBody.RootElement.GetProperty("state").GetString();
        var approvedEtag = approveResponse.Headers.ETag?.Tag ?? approveDraftEtag;

        // ── PATCH on an approved spot: refused (409) — editing an approved spot would invalidate a
        // render already claimed or already landed. ──
        var patchOnApprovedRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/ads/{approveDraftId}")
        {
            Content = JsonContent.Create(new { title = "Should never land" }),
        };
        patchOnApprovedRequest.Headers.TryAddWithoutValidation("If-Match", approvedEtag);
        var patchOnApprovedResponse = await client.SendAsync(patchOnApprovedRequest);
        PatchOnApprovedStatus = patchOnApprovedResponse.StatusCode;

        // ── Retry: a failed spot seeded directly via SQL (POST can never birth Failed) with a VALID
        // script, retried (also proves the gate lets a valid script through). ──
        var (retryId, retryVersion) = await AdsWireFixtures.InsertFailedSpotAsync(
            database.StationConnectionString, brand: "Retry Test Brand", script: OtherwiseValidScript);

        var retryRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/ads/{retryId}/retry");
        retryRequest.Headers.TryAddWithoutValidation("If-Match", $"W/\"{retryVersion}\"");
        var retryResponse = await client.SendAsync(retryRequest);
        RetryStatus = retryResponse.StatusCode;
        var retryBody = await JsonDocument.ParseAsync(await retryResponse.Content.ReadAsStreamAsync());
        RetryResultState = retryBody.RootElement.GetProperty("state").GetString();

        // ── Retry refuses a STILL-invalid script: a failed spot whose (unedited) script names a
        // blocklisted brand — the retry itself refuses with the rule id, never reaching the store. ──
        var (retryInvalidId, retryInvalidVersion) = await AdsWireFixtures.InsertFailedSpotAsync(
            database.StationConnectionString, brand: "Retry Invalid Brand", script: InvalidBrandCollisionScript);

        var retryInvalidRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/ads/{retryInvalidId}/retry");
        retryInvalidRequest.Headers.TryAddWithoutValidation("If-Match", $"W/\"{retryInvalidVersion}\"");
        var retryInvalidResponse = await client.SendAsync(retryInvalidRequest);
        RetryInvalidScriptStatus = retryInvalidResponse.StatusCode;
        var retryInvalidBody = await JsonDocument.ParseAsync(await retryInvalidResponse.Content.ReadAsStreamAsync());
        RetryInvalidScriptRuleId = retryInvalidBody.RootElement.TryGetProperty("ruleId", out var retryRuleIdProperty)
            ? retryRuleIdProperty.GetString() : null;

        // ── Approve refuses a brief-only draft: no script at all — a null script folds into the
        // SAME format-rule refusal an empty one hits. This draft stays draft (approve never reaches
        // the store), so it counts toward the list-paging fact's own total below. ──
        var briefOnlyResponse = await client.PostAsJsonAsync("/api/ads", new
        {
            brand = "Brief Only Brand",
            title = "Brief only spot",
            brief = "A brief with no script yet.",
            spotSeconds = 30,
        });
        var briefOnlyDraft = await JsonDocument.ParseAsync(await briefOnlyResponse.Content.ReadAsStreamAsync());
        var briefOnlyId = briefOnlyDraft.RootElement.GetProperty("id").GetInt64();
        var briefOnlyEtag = briefOnlyResponse.Headers.ETag?.Tag ?? "";

        var approveBriefOnlyRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/ads/{briefOnlyId}/approve");
        approveBriefOnlyRequest.Headers.TryAddWithoutValidation("If-Match", briefOnlyEtag);
        var approveBriefOnlyResponse = await client.SendAsync(approveBriefOnlyRequest);
        ApproveBriefOnlyStatus = approveBriefOnlyResponse.StatusCode;
        var approveBriefOnlyBody = await JsonDocument.ParseAsync(await approveBriefOnlyResponse.Content.ReadAsStreamAsync());
        ApproveBriefOnlyRuleId = approveBriefOnlyBody.RootElement.TryGetProperty("ruleId", out var approveRuleIdProperty)
            ? approveRuleIdProperty.GetString() : null;

        // ── Retire: a ready spot seeded directly via SQL (only the render worker births Ready,
        // deliberately not running in this factory), retired. ──
        var (retireId, retireVersion) = await AdsWireFixtures.InsertReadySpotAsync(
            database.StationConnectionString, brand: "Retire Test Brand", mediaId: 999_999);

        var retireRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/ads/{retireId}/retire");
        retireRequest.Headers.TryAddWithoutValidation("If-Match", $"W/\"{retireVersion}\"");
        var retireResponse = await client.SendAsync(retireRequest);
        RetireStatus = retireResponse.StatusCode;
        var retireBody = await JsonDocument.ParseAsync(await retireResponse.Content.ReadAsStreamAsync());
        RetireResultState = retireBody.RootElement.GetProperty("state").GetString();

        // ── The If-Match mutant transcript (T403 review finding 1): absent → 428, malformed → 400,
        // a real success, then the SAME (now stale) token → 409, all against one seeded draft. ──
        var ifMatchDraftResponse = await client.PostAsJsonAsync("/api/ads", new
        {
            brand = "If-Match Test Brand",
            title = "If-Match test spot",
            script = OtherwiseValidScript,
            spotSeconds = 30,
        });
        var ifMatchDraft = await JsonDocument.ParseAsync(await ifMatchDraftResponse.Content.ReadAsStreamAsync());
        var ifMatchDraftId = ifMatchDraft.RootElement.GetProperty("id").GetInt64();
        var ifMatchOriginalEtag = ifMatchDraftResponse.Headers.ETag?.Tag ?? "";

        var absentRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/ads/{ifMatchDraftId}/approve");
        var absentResponse = await client.SendAsync(absentRequest);
        IfMatchAbsentStatus = absentResponse.StatusCode;

        var malformedRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/ads/{ifMatchDraftId}/approve");
        malformedRequest.Headers.TryAddWithoutValidation("If-Match", "not-a-valid-xid-token");
        var malformedResponse = await client.SendAsync(malformedRequest);
        IfMatchMalformedStatus = malformedResponse.StatusCode;

        var validRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/ads/{ifMatchDraftId}/approve");
        validRequest.Headers.TryAddWithoutValidation("If-Match", ifMatchOriginalEtag);
        var validResponse = await client.SendAsync(validRequest);
        if (validResponse.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"the If-Match setup approve unexpectedly returned {validResponse.StatusCode}");

        var staleRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/ads/{ifMatchDraftId}/approve");
        staleRequest.Headers.TryAddWithoutValidation("If-Match", ifMatchOriginalEtag);
        var staleResponse = await client.SendAsync(staleRequest);
        StaleIfMatchStatus = staleResponse.StatusCode;

        // ── The list pages by state: three MORE drafts, purely for this fact — CleanDraftId, the
        // verbatim-quirk draft, and the brief-only draft (still draft; approve was refused) plus
        // these three make an exact total of 6. ──
        for (var i = 1; i <= 3; i++)
        {
            await client.PostAsJsonAsync("/api/ads", new
            {
                brand = $"List Fixture Brand {i}",
                title = $"List fixture spot {i}",
                brief = "A brief with no script yet.",
                spotSeconds = 30,
            });
        }

        var listResponse = await client.GetAsync("/api/ads?state=draft&limit=2&offset=0");
        DraftListStatus = listResponse.StatusCode;
        var listBody = await JsonDocument.ParseAsync(await listResponse.Content.ReadAsStreamAsync());
        var items = listBody.RootElement.GetProperty("items").EnumerateArray().ToList();
        DraftListItemCount = items.Count;
        DraftListTotal = listBody.RootElement.GetProperty("total").GetInt32();
        DraftListStates = items.Select(item => item.GetProperty("state").GetString() ?? "").ToList();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harness — WebApplicationFactory + ephemeral Postgres subclasses (the Story374/Story382
// "`file`-scoped types cannot cross files" precedent — this file supplies its own). ──

/// <summary>
/// Boots the real production composition root against a real ephemeral Postgres with every hosted
/// service removed — no <c>AdSpotWorker</c>/<c>AdSpotLifecycleGuardianService</c>/
/// <c>AdsLibrarySeedHostedService</c> reach, so this arc's own seeded rows (and the states its
/// verb-calls drive them through) are never raced or mutated by a background tick. Every
/// <c>AdsController</c> endpoint is still reachable — only the BACKGROUND loops are removed, the same
/// <c>Story382WebFactory</c>/<c>Story374</c> "real controllers, no hosted-service reach" idiom.
/// </summary>
file sealed class Story392AdsWebFactory(Story392AdsDatabase db) : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t403-ads-api";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
        });
    }
}

/// <summary>
/// STORY-392 AC6's own DB-less factory — a bogus <c>ConnectionStrings:*</c> (never actually reached:
/// <c>Admin:Enabled=false</c> 404s in <c>SurfaceGateMiddleware</c>, BEFORE routing ever reaches
/// <c>AdsController</c>'s constructor) — no real ephemeral Postgres needed just to prove a 404 (the
/// <c>Story374.GardenerSurfaceWebFactory</c>/<c>Story166.KillSwitchWebFactory</c> precedent).
/// </summary>
file sealed class AdsAdminOffWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Admin:Enabled", "false");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-t403-ads-admin-off");
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
        });
    }
}

/// <summary>
/// This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/> harness — see
/// that type's own remarks for the full "which compose file, why a unique project name + OS-assigned
/// port" rationale. Supplies only the <c>"genwave-t403"</c> compose project-name prefix this file's
/// own arc needs.
/// </summary>
file sealed class Story392AdsDatabase : EphemeralStationDatabase
{
    Story392AdsDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story392AdsDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t403");
        var db = new Story392AdsDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>Arrange helpers this file's own arc uses — raw SQL against the ephemeral database's own
/// connection strings, never through <c>AdSpotRepository</c>/<c>IAdminMediaLookup</c> (the
/// <c>GardenerRotFixtures</c> precedent: an independent seed for states the API itself can never
/// produce directly — POST only births Draft; only the render worker, deliberately not running in this
/// factory, births Ready).</summary>
public static class AdsWireFixtures
{
    public static async Task<long> InsertPlayableMediaRowAsync(
        string libraryConnectionString, string path, string title, string artist)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into library.media (path, format, size_bytes, mtime, state, duration_ms, title, artist, eligible)
            values (@path, 'flac', 1024, now(), 'ready', 200000, @title, @artist, true)
            returning id
            """;
        cmd.Parameters.AddWithValue("path", path);
        cmd.Parameters.AddWithValue("title", title);
        cmd.Parameters.AddWithValue("artist", artist);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }

    public static async Task<(long Id, string Version)> InsertFailedSpotAsync(
        string stationConnectionString, string brand, string script)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into station.ad_spot (brand, title, script, source, spot_seconds, state, fail_reason)
            values (@brand, @brand || ' spot', @script, 'llm'::station.ad_source, 30, 'failed'::station.ad_state, 'tts_timeout')
            returning id, xmin::text as version
            """;
        cmd.Parameters.AddWithValue("brand", brand);
        cmd.Parameters.AddWithValue("script", script);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt64(0), reader.GetString(1));
    }

    public static async Task<(long Id, string Version)> InsertReadySpotAsync(
        string stationConnectionString, string brand, long mediaId)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into station.ad_spot (brand, title, script, source, spot_seconds, state, media_id)
            values (@brand, @brand || ' spot', 'ANNOUNCER: Ready to air.', 'llm'::station.ad_source, 30, 'ready'::station.ad_state, @mediaId)
            returning id, xmin::text as version
            """;
        cmd.Parameters.AddWithValue("brand", brand);
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt64(0), reader.GetString(1));
    }
}
