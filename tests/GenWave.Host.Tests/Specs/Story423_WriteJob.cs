// STORY-423 — "Write it for me" runs as a job (SPEC F174.3 · PLAN T441)
//
// BDD specification — xUnit through the deployed entry point (WebApplicationFactory<Program> against a
// real ephemeral Postgres — the Story422_AdSpotJobs.cs/Story412_SpotBelongsToSponsor.cs arc idiom): every
// fact drives POST /api/ads/{id}/write (plus POST /api/sponsors + POST /api/ads to arrange the
// sponsors/spots it needs, and POST /api/ads/{id}/approve for AC3's own "outside draft" arrangement) over
// HTTP with an authed admin session, never AdSpotJobService/AdScriptWriter/AdsController directly. The
// real AdScriptWriter runs inside the real AdSpotJobService for every fact here — only the ONE outgoing
// LLM completions call it makes is faked (AdScriptCompletionsRouter, Support/), so this suite proves the
// write job's OWN wiring (fetch → build request → call the writer → land script or fail), not the
// writer's own internal prompt/validation logic (already proven in GenWave.Ads.Tests).

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Ads;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureWriteItForMeRunsAsAJob
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(Story423Collection.Name)]
    public sealed class ScenarioPostWriteEnqueues(Story423Arc arc)
    {
        [Fact]
        public void WriteIs202()
            => Assert.Equal(HttpStatusCode.Accepted, arc.EnqueueStatus);

        [Fact]
        public void TheRowHasJobKindWriteAndAStartedAt()
        {
            Assert.Equal("write", arc.EnqueueResponseJobKind);
            Assert.NotNull(arc.EnqueueResponseJobStartedAt);
        }
    }

    [Collection(Story423Collection.Name)]
    public sealed class ScenarioTheScriptLandsOnTheRowOnSuccess(Story423Arc arc)
    {
        [Fact]
        public void ScriptEqualsTheWritersOutput()
            => Assert.Equal(AdScriptCompletionsRouter.WellFormedReply, arc.SettledScript);

        [Fact]
        public void TheDatabaseRowItselfCarriesTheWritersOutput()
            // Read straight out of station.ad_spot rather than through the same API layer the GET-based
            // fact above already went through — the row IS the fact, not just what one reader echoes.
            => Assert.Equal(AdScriptCompletionsRouter.WellFormedReply, arc.SettledScriptFromSql);

        [Fact]
        public void SourceIsUnchanged()
            => Assert.Equal("owner", arc.SettledSource);

        [Fact]
        public void VoicePlanIsStillNull()
            => Assert.True(arc.SettledVoicePlanIsNull);

        [Fact]
        public void JobIsNullAfterCompletion()
        {
            Assert.True(arc.SuccessJobSettled, "the successful write never cleared its own job stamp");
            Assert.False(arc.SettledJobIsPresent);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    [Collection(Story423Collection.Name)]
    public sealed class ScenarioRejectingAndFailing(Story423Arc arc)
    {
        [Fact]
        public void WriteOutsideDraftIs409AdWriteNotDraft()
        {
            Assert.Equal(HttpStatusCode.Conflict, arc.NotDraftWriteStatus);
            Assert.Equal("ad_write_not_draft", arc.NotDraftWriteType);
        }

        [Fact]
        public void WriterFailureStampsJobErrorAndClearsJobKind()
        {
            // Carries the arrangement's own poll result: whether the row EVER reached the
            // job-present-with-an-error shape at all, asserted first so a broken poll fails here by
            // assertion rather than by GetSpotAsync's own repeated 10-second timeout crashing every
            // other fact in this Scenario before any of them get to run their own body.
            Assert.True(arc.FailedJobBecameVisible, "the failing spot's row never became visible with job.kind: null and a job.error");

            Assert.Null(arc.FailedJobKind);

            // Pinned to the EXACT reason AdScriptParser's own Format rule produces for the arranged
            // 900-char line against the default Llm:MaxCopyChars (450) — not merely a substring/shape
            // check, so a fixture drifting off the failure this fact actually claims (wrong rule, wrong
            // count, or a reason that silently changed) fails loudly instead of continuing to pass on
            // any old text that happens to omit a newline.
            Assert.Equal("the ANNOUNCER line (900 chars) exceeds the 450-char per-line budget", arc.FailedJobError);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class Story423Collection : ICollectionFixture<Story423Arc>
{
    public const string Name = "Story423WriteJob";
}

/// <summary>
/// Arranges every fact STORY-423's Scenarios read, over the real production HTTP pipeline with a real
/// admin session and the real <see cref="AdSpotJobService"/> running as a hosted service (the
/// Story422Arc precedent) — the write job's own <see cref="GenWave.Tts.AdScriptWriter"/> call is real
/// too, routed to <see cref="AdScriptCompletionsRouter"/> in place of a live LLM endpoint.
/// </summary>
public sealed class Story423Arc : IAsyncLifetime
{
    public HttpStatusCode EnqueueStatus { get; private set; }
    public string? EnqueueResponseJobKind { get; private set; }
    public DateTime? EnqueueResponseJobStartedAt { get; private set; }

    public bool SuccessJobSettled { get; private set; }
    public string? SettledScript { get; private set; }
    public string? SettledScriptFromSql { get; private set; }
    public string? SettledSource { get; private set; }
    public bool SettledVoicePlanIsNull { get; private set; }
    public bool SettledJobIsPresent { get; private set; }

    public HttpStatusCode NotDraftWriteStatus { get; private set; }
    public string? NotDraftWriteType { get; private set; }

    public bool FailedJobBecameVisible { get; private set; }
    public string? FailedJobKind { get; private set; }
    public string? FailedJobError { get; private set; }

    public async Task InitializeAsync()
    {
        // A local, not a field — Story423Database is file-local (CS9051), the Story422Database/
        // Story412Database precedent.
        await using var database = await Story423Database.StartAsync();
        var router = new AdScriptCompletionsRouter();
        await using var factory = new Story423WebFactory(database, router);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Story423WebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        var gate = factory.Services.GetRequiredService<GenWave.Host.Playout.OnAirRenderGate>();
        var sponsorId = await AdSpotJobTestHelpers.CreateSponsorAsync(client, "Larkspur & Loom Mercantile");

        // ── AC1 — the 202 response itself already carries the freshly-stamped kind/startedAt (PLAN
        // T441: TryEnqueueAsync stamps the row BEFORE the controller re-fetches it for the response
        // body). Held under the on-air gate so the job cannot possibly race past its own wait loop and
        // clear the stamp before this arrangement reads it. ──
        var enqueueSpotId = await AdSpotJobTestHelpers.CreateDraftSpotAsync(
            client, sponsorId, "Enqueue spot", "A short deal proving the enqueue stamp.");
        gate.Enter();
        try
        {
            var enqueueResponse = await AdSpotJobTestHelpers.PostWriteAsync(client, enqueueSpotId);
            EnqueueStatus = enqueueResponse.StatusCode;
            var enqueueBody = await JsonDocument.ParseAsync(await enqueueResponse.Content.ReadAsStreamAsync());
            EnqueueResponseJobKind = AdSpotJobTestHelpers.JobKind(enqueueBody.RootElement);
            EnqueueResponseJobStartedAt = AdSpotJobTestHelpers.JobStartedAt(enqueueBody.RootElement);
        }
        finally
        {
            gate.Exit();
        }

        // No fact reads the enqueue spot's own settled state past this point — the wait below only
        // keeps its background write from racing this Arc's own teardown (the Story422Arc precedent),
        // so a stalled write here cannot crash every other Scenario in this collection through a bare
        // timeout; it just leaves this discarded flag false.
        await AdSpotJobTestHelpers.TryPollUntilAsync(
            client, enqueueSpotId, AdSpotJobTestHelpers.JobIsSettled, TimeSpan.FromSeconds(10));

        // ── AC2 — a write that actually completes lands the writer's own script, leaves source/
        // voicePlan untouched, and clears the job stamp entirely (job: null, not merely kind: null). ──
        var successSpotId = await AdSpotJobTestHelpers.CreateDraftSpotAsync(
            client, sponsorId, "Success spot", "A short deal that writes cleanly.");
        var enqueueSuccess = await AdSpotJobTestHelpers.PostWriteAsync(client, successSpotId);
        if (enqueueSuccess.StatusCode != HttpStatusCode.Accepted)
            throw new InvalidOperationException($"arrange: POST /api/ads/{successSpotId}/write unexpectedly returned {enqueueSuccess.StatusCode}");

        // TryPollUntilAsync, not PollUntilAsync — JobIsNullAfterCompletion asserts SuccessJobSettled
        // FIRST, so a write that never clears its own stamp dies at that fact's own assertion instead
        // of a fixture-wide TimeoutException that stops every other fact below from ever running.
        var (successSettled, settled) = await AdSpotJobTestHelpers.TryPollUntilAsync(
            client, successSpotId, AdSpotJobTestHelpers.JobIsSettled, TimeSpan.FromSeconds(10));
        SuccessJobSettled = successSettled;
        SettledScript = settled.GetProperty("script").GetString();
        SettledScriptFromSql = await AdSpotJobTestHelpers.ReadAdSpotScriptAsync(database.StationConnectionString, successSpotId);
        SettledSource = settled.GetProperty("source").GetString();
        SettledVoicePlanIsNull = settled.GetProperty("voicePlan").ValueKind == JsonValueKind.Null;
        SettledJobIsPresent = AdSpotJobTestHelpers.JobIsPresent(settled);

        // ── AC3 — a write is refused as 409 ad_write_not_draft once the spot has moved past Draft.
        // Created WITH a valid script (the only way approve has anything of its own to re-validate),
        // then approved for real through POST /api/ads/{id}/approve. ──
        const string approvableScript =
            "ANNOUNCER: Larkspur & Loom Mercantile has a deal so good it's almost illegal.\n" +
            "ANNOUNCER: Call 555-0177 - that's 555-0177 - Larkspur & Loom.";
        var (approvedSpotId, approvedEtag) = await AdSpotJobTestHelpers.CreateDraftSpotWithScriptAsync(
            client, sponsorId, "Already approved spot", approvableScript);

        var approveRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/ads/{approvedSpotId}/approve");
        approveRequest.Headers.TryAddWithoutValidation("If-Match", approvedEtag);
        var approveResponse = await client.SendAsync(approveRequest);
        if (approveResponse.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"arrange: POST /api/ads/{approvedSpotId}/approve unexpectedly returned {approveResponse.StatusCode}");

        var notDraftWriteResponse = await AdSpotJobTestHelpers.PostWriteAsync(client, approvedSpotId);
        NotDraftWriteStatus = notDraftWriteResponse.StatusCode;
        var notDraftWriteBody = await JsonDocument.ParseAsync(await notDraftWriteResponse.Content.ReadAsStreamAsync());
        NotDraftWriteType = notDraftWriteBody.RootElement.TryGetProperty("type", out var notDraftType)
            ? notDraftType.GetString() : null;

        // ── AC4 — a writer failure (a well-formed completion whose one line runs far over
        // Llm:MaxCopyChars on EVERY ask, so the validator's own Format rule refuses both the original
        // ask and the re-ask — never a re-ask ladder that eventually succeeds) stamps job_error and
        // clears job_kind, the row surviving with job.kind: null and job.error non-null (never job:
        // null the way a SUCCESS clears it). Routed by a UNIQUE sponsor name so this sponsor's own
        // completion call, and only this one, is forced to fail (SPEC F174.8 — AdScriptPromptBuilder
        // embeds "Sponsor: {name}" as the first prompt line, the router's own key). ──
        // A plain-ASCII name, no punctuation the default System.Text.Json encoder would escape
        // (ChatCompletionRequestJson.Options never sets its own Encoder, so it falls back to
        // JavaScriptEncoder.Default — the conservative HTML-safe encoder that rewrites an ampersand
        // as the six-character escape sequence \u0026 in the outgoing request body), which would
        // silently break this router's own plain-text marker match below.
        const string failingSponsorName = "Ferrous Finch Ironworks";

        // AdScriptWriter.ApplyLineAwareHygiene only ever runs LlmCopyWriter.ApplyCopyHygiene per line
        // (trim/preamble/quote/newline/bracket/emphasis/whitespace) — never the separate,
        // length-capping LlmCopyWriter.CleanCopy path other callers use — so this 900-char single line
        // reaches AdScriptValidator's own Format rule UNTRUNCATED on every ask, forcing the exact,
        // deterministic refusal reason AC4 pins below (the default Llm:MaxCopyChars this factory
        // leaves unset is 450).
        var overLongLine = "ANNOUNCER: " + new string('A', 900);
        router.RouteSponsor(failingSponsorName, _ =>
            Task.FromResult(AdScriptCompletionsRouter.CompletionsResponse(overLongLine)));
        var failingSponsorId = await AdSpotJobTestHelpers.CreateSponsorAsync(client, failingSponsorName);
        var failingSpotId = await AdSpotJobTestHelpers.CreateDraftSpotAsync(
            client, failingSponsorId, "Failing spot", "A short deal whose writer call fails outright.");

        var enqueueFailing = await AdSpotJobTestHelpers.PostWriteAsync(client, failingSpotId);
        if (enqueueFailing.StatusCode != HttpStatusCode.Accepted)
            throw new InvalidOperationException($"arrange: POST /api/ads/{failingSpotId}/write unexpectedly returned {enqueueFailing.StatusCode}");

        // A non-throwing poll (PLAN T441): a mutation that stops a failed job's own error ever
        // becoming visible over GET must fail THIS Scenario's own flag assertion, not crash every
        // fact in it via PollUntilAsync's own TimeoutException before any of them run.
        var (failedJobBecameVisible, failedBody) = await AdSpotJobTestHelpers.TryPollUntilAsync(
            client, failingSpotId,
            body => AdSpotJobTestHelpers.JobIsPresent(body) && AdSpotJobTestHelpers.JobError(body) is not null,
            TimeSpan.FromSeconds(10));
        FailedJobBecameVisible = failedJobBecameVisible;
        FailedJobKind = AdSpotJobTestHelpers.JobKind(failedBody);
        FailedJobError = AdSpotJobTestHelpers.JobError(failedBody);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harness — WebApplicationFactory + ephemeral Postgres subclasses (the Story422_AdSpotJobs.cs/
// Story412_SpotBelongsToSponsor.cs "`file`-scoped types cannot cross files" precedent — this file
// supplies its own). ──

file sealed class Story423WebFactory(EphemeralStationDatabase db, AdScriptCompletionsRouter router)
    : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t441-write-job";

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
        builder.UseSetting("Ads:JobQueueCapacity", "4");
        builder.UseSetting("Llm:Endpoint", "http://fake-llm.local");
        builder.UseSetting("Llm:Model", "test-model");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddHostedService(sp => sp.GetRequiredService<AdSpotJobService>());

            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(router.Handler));
        });
    }
}

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/> harness
/// — see that type's own remarks. Supplies only the <c>"genwave-t441c"</c> compose project-name prefix
/// this file's own arc needs.</summary>
file sealed class Story423Database : EphemeralStationDatabase
{
    Story423Database(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story423Database> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t441c");
        var db = new Story423Database(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}
