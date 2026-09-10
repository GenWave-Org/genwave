// STORY-422 — One job at a time, waits for the station, cancellable (SPEC F174.2 · PLAN T441)
//
// BDD specification — xUnit through the deployed entry point (WebApplicationFactory<Program> against a
// real ephemeral Postgres, the Story412_SpotBelongsToSponsor.cs idiom): every fact drives POST/DELETE
// /api/ads/{id}/write|job (and POST /api/sponsors + POST /api/ads to arrange the sponsors/spots it
// needs) over HTTP with an authed admin session, never AdSpotJobService/AdsController directly, EXCEPT
// for the one real OnAirRenderGate this suite resolves straight out of the factory's own DI container
// (factory.Services.GetRequiredService<OnAirRenderGate>()) to simulate "the station is on air right
// now" — the same concrete singleton PlayoutFeederService itself calls Enter()/Exit() around, dual-
// registered as IOnAirRenderSignal (PlayoutServiceCollectionExtensions.cs) so AdSpotJobService's own
// reads of IOnAirRenderSignal.InFlight see exactly what this suite drives.
//
// PLAN T441 ruling (a): STORY-422 AC4's own "kind:\"preview\"" example is pinned here as kind:"write"
// — this task builds ONLY the write job (T442 adds "preview"); the job OBJECT SHAPE is what AC4 pins,
// not which kind populates it.
//
// TWO Arcs, not one (a Channel's own bound is fixed at construction — see AdSpotJobService's own
// remarks): Story422Arc (Ads:JobQueueCapacity=4) covers every fact except AC3, which needs a queue
// that is provably FULL at exactly one already-queued item — Story422QueueFullArc alone
// (Ads:JobQueueCapacity=1) covers that. Both fixtures ride the SAME collection (xUnit's own
// multi-ICollectionFixture-per-collection support) so a Scenario takes only the fixture(s) it needs.

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
using GenWave.Host.Playout;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureOneJobAtATimeWaitsForTheStationCancellable
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(Story422Collection.Name)]
    public sealed class ScenarioOneJobAtATimeSecondIsQueued(Story422Arc arc)
    {
        [Fact]
        public void BothEnqueuesAre202()
        {
            Assert.Equal(HttpStatusCode.Accepted, arc.EnqueueAStatus);
            Assert.Equal(HttpStatusCode.Accepted, arc.EnqueueBStatus);
        }

        [Fact]
        public void BothRowsAreStampedAsSoonAsTheyAreEnqueued()
        {
            Assert.True(arc.ARowWasStampedAfterEnqueue);
            Assert.True(arc.BRowWasStampedAfterEnqueue);
        }

        [Fact]
        public void OnlyOneRowIsRunningAtAnyMoment()
            // PLAN T441 ruling on this name: at most one job RUNS at a time = the handler's observed
            // max concurrency is 1 while both rows are stamped. A gated completions route records the
            // requests' own peak concurrency directly — the ONE claim IsWaitingForStation cannot make
            // (it reports at most one spot id waiting by construction, so it is tautological evidence
            // of "at most one job at a time" and proves nothing about whether a second job's own
            // request could reach the writer concurrently).
            => Assert.Equal(1, arc.MaxHandlerConcurrency);
    }

    [Collection(Story422Collection.Name)]
    public sealed class ScenarioTheRowSurfacesTheJobState(Story422Arc arc)
    {
        [Fact]
        public void GetCarriesJobKindStartedAtWaitingAndError()
        {
            Assert.Equal("write", arc.StateJobKind);
            Assert.True(arc.StateJobHasStartedAt);
            Assert.True(arc.StateJobWaitingForStation);
            Assert.Null(arc.StateJobError);
        }
    }

    [Collection(Story422Collection.Name)]
    public sealed class ScenarioWaitingForKokoroShowsInTheRow(Story422Arc arc)
    {
        [Fact]
        public void WaitingForStationIsTrueWhileInFlight()
            => Assert.True(arc.WaitingWhileInFlight);

        [Fact]
        public void TheRenderProceedsOnceInFlightClears()
            => Assert.True(arc.ProceededAfterInFlightCleared, "the write never settled after the station cleared");

        [Fact]
        public void WaitingForStationIsFalseOnceTheWriteIsRunning()
        {
            Assert.True(arc.RunningButNotWaitingOnceInFlightCleared,
                "the row kept reporting waitingForStation after the station cleared and the write was running");
            Assert.True(arc.WriteReachedTheWriterWhileNotWaiting,
                "the write never reached the completions endpoint while waitingForStation read false");
        }
    }

    [Collection(Story422Collection.Name)]
    public sealed class ScenarioCancelClearsTheStamps(Story422Arc arc)
    {
        [Fact]
        public void DeleteJobIs204()
            => Assert.Equal(HttpStatusCode.NoContent, arc.DeleteJobStatus);

        [Fact]
        public void TheRowShowsJobNull()
            => Assert.True(arc.JobIsNullRightAfterCancel);

        [Fact]
        public void TheRunningJobsTokenWasCancelled()
        {
            Assert.True(
                arc.CancelJobWasParkedOnTheStation,
                "the cancelled job was never parked on the station, so this scenario cancelled a queued job instead of a running one");

            // The job was mid-wait (station on air) when DELETE ran; the station cleared moments
            // later. A correctly cancelled token means that job never reaches its own completion
            // call — proven as "no NEW completion request arrived" across a bounded wait AFTER the
            // station cleared, against a request-count baseline captured before DELETE ran.
            Assert.True(arc.NoNewRequestArrivedAfterCancel);
        }
    }

    // AC7 is a pure reflection scan — no HTTP call, no Arc/fixture needed at all (the
    // Story353_LlmCauseTaxonomy.cs ScenarioCountersRoll "PURE level" precedent).
    public sealed class ScenarioTheRunnerLivesInGenWaveAds
    {
        [Fact]
        public void AdSpotJobServiceAssemblyIsGenWaveAds()
            => Assert.Equal("GenWave.Ads", typeof(AdSpotJobService).Assembly.GetName().Name);
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    [Collection(Story422Collection.Name)]
    public sealed class ScenarioRejectingWhenBusyOrFull(Story422Arc arc, Story422QueueFullArc queueFullArc)
    {
        [Fact]
        public void ASecondJobOnTheSameSpotIs409AdJobBusy()
        {
            Assert.Equal(HttpStatusCode.Conflict, arc.SecondWriteSameSpotStatus);
            Assert.Equal("ad_job_busy", arc.SecondWriteSameSpotType);
        }

        [Fact]
        public void AFullQueueIs429AdJobQueueFull()
        {
            Assert.Equal(HttpStatusCode.Accepted, queueFullArc.FirstWriteStatus);
            Assert.Equal(HttpStatusCode.Accepted, queueFullArc.SecondWriteStatus);
            Assert.Equal((HttpStatusCode)429, queueFullArc.ThirdWriteStatus);
            Assert.Equal("ad_job_queue_full", queueFullArc.ThirdWriteType);
        }

        [Fact]
        public void TheRefusedSpotIsNotStamped()
            => Assert.False(queueFullArc.ThirdSpotIsStamped);
    }

    [Collection(Story422Collection.Name)]
    public sealed class ScenarioUnknownOrUnauthenticatedRoutesAreRefused(Story422Arc arc)
    {
        [Fact]
        public void WriteOnAnUnknownIdIs404()
            => Assert.Equal(HttpStatusCode.NotFound, arc.WriteUnknownIdStatus);

        [Fact]
        public void DeleteJobOnAnUnknownIdIs404()
            => Assert.Equal(HttpStatusCode.NotFound, arc.DeleteJobUnknownIdStatus);

        [Fact]
        public void DeleteJobOnASpotWithNoJobIs204AndTheRowStaysNull()
        {
            Assert.Equal(HttpStatusCode.NoContent, arc.DeleteJobOnUnjobbedSpotStatus);
            Assert.True(arc.JobIsNullAfterIdempotentDelete);
        }

        [Fact]
        public void AnUnauthenticatedCallerIs401OnBothRoutes()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, arc.UnauthenticatedWriteStatus);
            Assert.Equal(HttpStatusCode.Unauthorized, arc.UnauthenticatedDeleteJobStatus);
        }
    }
}

// ── Collection definition — TWO fixtures, ONE collection (xUnit's own multi-ICollectionFixture-per-
// collection support): every Scenario above takes only the fixture(s) it actually needs. ──

[CollectionDefinition(Name)]
public sealed class Story422Collection : ICollectionFixture<Story422Arc>, ICollectionFixture<Story422QueueFullArc>
{
    public const string Name = "Story422OneJobAtATime";
}

/// <summary>
/// Arranges every AC1/AC2/AC4/AC5/AC6 fact STORY-422's Scenarios read, over the real production HTTP
/// pipeline against a real ephemeral Postgres, with the real <see cref="AdSpotJobService"/> running as
/// a hosted service (re-added after <c>RemoveAll&lt;IHostedService&gt;()</c> strips every OTHER hosted
/// service — the Story412Arc precedent, extended: this suite is the one place in this test project that
/// genuinely needs its OWN hosted service alive). Every arrangement step below runs to full completion
/// (every enqueued job is settled — <c>job: null</c> or a stamped error — before the next step starts,
/// and certainly before <see cref="InitializeAsync"/> returns) since the ephemeral Postgres and the
/// <see cref="WebApplicationFactory{TEntryPoint}"/> both go out of scope, and are disposed, the instant
/// this method returns (the Story412Arc "arrange once, capture properties" idiom) — a background job
/// still running past that point would race its own teardown.
/// </summary>
public sealed class Story422Arc : IAsyncLifetime
{
    const string SponsorName = "Thistlebound Kettle Corn";
    const string CancelSponsorName = "Fenwick's Notions";

    // A plain-ASCII name, no punctuation the default System.Text.Json encoder would escape
    // (ChatCompletionRequestJson.Options never sets its own Encoder, so it falls back to
    // JavaScriptEncoder.Default, which rewrites an ampersand as the six-character escape sequence
    // \u0026 in the outgoing request body) — that would silently break this router's own plain-text
    // marker match.
    const string WaitSponsorName = "Larkspur Cedarwood Trading";

    public HttpStatusCode EnqueueAStatus { get; private set; }
    public HttpStatusCode EnqueueBStatus { get; private set; }
    public bool ARowWasStampedAfterEnqueue { get; private set; }
    public bool BRowWasStampedAfterEnqueue { get; private set; }
    public int MaxHandlerConcurrency { get; private set; }

    public string? StateJobKind { get; private set; }
    public bool StateJobHasStartedAt { get; private set; }
    public bool StateJobWaitingForStation { get; private set; }
    public string? StateJobError { get; private set; }

    public bool WaitingWhileInFlight { get; private set; }
    public bool RunningButNotWaitingOnceInFlightCleared { get; private set; }
    public bool WriteReachedTheWriterWhileNotWaiting { get; private set; }
    public bool ProceededAfterInFlightCleared { get; private set; }

    public HttpStatusCode DeleteJobStatus { get; private set; }
    public bool CancelJobWasParkedOnTheStation { get; private set; }
    public bool JobIsNullRightAfterCancel { get; private set; }
    public bool NoNewRequestArrivedAfterCancel { get; private set; }

    public HttpStatusCode SecondWriteSameSpotStatus { get; private set; }
    public string? SecondWriteSameSpotType { get; private set; }

    public HttpStatusCode WriteUnknownIdStatus { get; private set; }
    public HttpStatusCode DeleteJobUnknownIdStatus { get; private set; }
    public HttpStatusCode DeleteJobOnUnjobbedSpotStatus { get; private set; }
    public bool JobIsNullAfterIdempotentDelete { get; private set; }
    public HttpStatusCode UnauthenticatedWriteStatus { get; private set; }
    public HttpStatusCode UnauthenticatedDeleteJobStatus { get; private set; }

    public async Task InitializeAsync()
    {
        // Locals, not fields — Story422Database/Story422WebFactory are file-local (CS9051), the
        // Story412Database/Story412WebFactory precedent.
        await using var database = await Story422Database.StartAsync();
        var router = new AdScriptCompletionsRouter();
        await using var factory = new Story422WebFactory(database, router, jobQueueCapacity: 4);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Story422WebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        var gate = factory.Services.GetRequiredService<OnAirRenderGate>();
        var sponsorId = await AdSpotJobTestHelpers.CreateSponsorAsync(client, SponsorName);

        // ── AC1 — two enqueues against two DIFFERENT drafts both answer 202 and both land a stamped
        // row immediately; only ONE of them is EVER actually inside the writer at a time. Proven with
        // IOnAirRenderSignal left OFF (InFlight == false) throughout, through a gated completions route
        // that records the requests' own peak concurrency — never through IOnAirRenderSignal itself,
        // which reports at most one spot id waiting by construction and so cannot distinguish "only one
        // job ever runs" from "only one job is ever reported". ──
        var gatedRoute = router.RouteGated(SponsorName);
        var spotAId = await AdSpotJobTestHelpers.CreateDraftSpotAsync(client, sponsorId, "Spot A", "A short deal for regulars.");
        var spotBId = await AdSpotJobTestHelpers.CreateDraftSpotAsync(client, sponsorId, "Spot B", "A short deal for newcomers.");

        EnqueueAStatus = (await AdSpotJobTestHelpers.PostWriteAsync(client, spotAId)).StatusCode;
        EnqueueBStatus = (await AdSpotJobTestHelpers.PostWriteAsync(client, spotBId)).StatusCode;

        ARowWasStampedAfterEnqueue = AdSpotJobTestHelpers.JobIsPresent(await AdSpotJobTestHelpers.GetSpotAsync(client, spotAId));
        BRowWasStampedAfterEnqueue = AdSpotJobTestHelpers.JobIsPresent(await AdSpotJobTestHelpers.GetSpotAsync(client, spotBId));

        // A genuine precondition — the handler must see at least one request before there is anything
        // to measure concurrency over — so this bound throws rather than reporting a false "never
        // overlapped" for an implementation that never dispatches at all.
        var sawFirstRequest = false;
        for (var i = 0; i < 100 && !sawFirstRequest; i++)
        {
            sawFirstRequest = gatedRoute.RequestsSeen >= 1;
            if (!sawFirstRequest)
                await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
        if (!sawFirstRequest)
            throw new InvalidOperationException("arrange: the gated completions route never saw a request for spot A or B");

        // B's own bounded chance to arrive at the writer concurrently with A — stops early only once
        // concurrency is actually observed at 2; a correct one-job-at-a-time runner never reaches that,
        // so this ordinarily spends its whole bound here without ever finding it.
        for (var i = 0; i < 20 && gatedRoute.MaxConcurrency < 2; i++)
            await Task.Delay(TimeSpan.FromMilliseconds(50));

        MaxHandlerConcurrency = gatedRoute.MaxConcurrency;
        gatedRoute.Release();

        // No fact reads spot A's or spot B's own settled state past this point — the wait below only
        // keeps their background writes from racing this Arc's own teardown (see this class's remarks),
        // so a stalled write here cannot crash every other Scenario in this collection through a bare
        // timeout; it just leaves this discarded flag false.
        await AdSpotJobTestHelpers.TryPollUntilAsync(client, spotAId, AdSpotJobTestHelpers.JobIsSettled, TimeSpan.FromSeconds(10));
        await AdSpotJobTestHelpers.TryPollUntilAsync(client, spotBId, AdSpotJobTestHelpers.JobIsSettled, TimeSpan.FromSeconds(10));

        // ── AC4 — GET carries kind/startedAt/waitingForStation/error together while the job is stuck
        // waiting on the station (never reaches RunWriteAsync, so no completion call is involved). ──
        var stateSpotId = await AdSpotJobTestHelpers.CreateDraftSpotAsync(client, sponsorId, "Spot C", "A short deal for the state check.");
        gate.Enter();
        try
        {
            var enqueue = await AdSpotJobTestHelpers.PostWriteAsync(client, stateSpotId);
            if (enqueue.StatusCode != HttpStatusCode.Accepted)
                throw new InvalidOperationException($"arrange: POST /api/ads/{stateSpotId}/write unexpectedly returned {enqueue.StatusCode}");

            var (ready, waiting) = await AdSpotJobTestHelpers.TryPollUntilAsync(
                client, stateSpotId, body => AdSpotJobTestHelpers.JobWaitingForStation(body) == true, TimeSpan.FromSeconds(5));
            StateJobKind = AdSpotJobTestHelpers.JobKind(waiting);
            StateJobHasStartedAt = AdSpotJobTestHelpers.JobStartedAt(waiting) is not null;
            StateJobWaitingForStation = ready && AdSpotJobTestHelpers.JobWaitingForStation(waiting) == true;
            StateJobError = AdSpotJobTestHelpers.JobError(waiting);
        }
        finally
        {
            gate.Exit();
        }

        // No fact reads spot C's own settled state past this point — the wait below only keeps its
        // background write from racing this Arc's own teardown (see this class's remarks), so a stalled
        // write here cannot crash every other Scenario in this collection through a bare timeout; it
        // just leaves this discarded flag false.
        await AdSpotJobTestHelpers.TryPollUntilAsync(client, stateSpotId, AdSpotJobTestHelpers.JobIsSettled, TimeSpan.FromSeconds(10));

        // ── AC5 — waitingForStation is true while the station is on air; once the station clears the
        // row keeps reporting kind:"write" but waitingForStation reads false WHILE the write is still
        // running, and the write itself actually proceeds (job settles to null) once it finishes. Spot
        // D gets its own sponsor and its own gated completions route (PLAN T441 ruling) so the
        // writer's own completion request blocks inside that route exactly the way AC1's gate does —
        // "running, not waiting for the station" becomes a state this Arc can hold open long enough to
        // observe, not a race it might simply miss. ──
        var waitRoute = router.RouteGated(WaitSponsorName);
        var waitSponsorId = await AdSpotJobTestHelpers.CreateSponsorAsync(client, WaitSponsorName);
        var waitSpotId = await AdSpotJobTestHelpers.CreateDraftSpotAsync(client, waitSponsorId, "Spot D", "A short deal that waits for air.");
        gate.Enter();
        try
        {
            var enqueue = await AdSpotJobTestHelpers.PostWriteAsync(client, waitSpotId);
            if (enqueue.StatusCode != HttpStatusCode.Accepted)
                throw new InvalidOperationException($"arrange: POST /api/ads/{waitSpotId}/write unexpectedly returned {enqueue.StatusCode}");

            var (ready, waiting) = await AdSpotJobTestHelpers.TryPollUntilAsync(
                client, waitSpotId, body => AdSpotJobTestHelpers.JobWaitingForStation(body) == true, TimeSpan.FromSeconds(5));
            WaitingWhileInFlight = ready && AdSpotJobTestHelpers.JobWaitingForStation(waiting) == true;
        }
        finally
        {
            gate.Exit();
        }

        try
        {
            var (runningReady, running) = await AdSpotJobTestHelpers.TryPollUntilAsync(
                client, waitSpotId,
                body => AdSpotJobTestHelpers.JobKind(body) == "write" && AdSpotJobTestHelpers.JobWaitingForStation(body) == false,
                TimeSpan.FromSeconds(5));
            RunningButNotWaitingOnceInFlightCleared = runningReady &&
                AdSpotJobTestHelpers.JobKind(running) == "write" && AdSpotJobTestHelpers.JobWaitingForStation(running) == false;

            // A bounded, non-throwing wait for the writer's own completion request to arrive at the
            // gated route (the AC1 arrangement's own 20x50ms shape) — waitingForStation reading false
            // is not itself proof the write reached the writer; this is the second, independent proof.
            for (var i = 0; i < 20 && waitRoute.RequestsSeen < 1; i++)
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            WriteReachedTheWriterWhileNotWaiting = waitRoute.RequestsSeen >= 1;
        }
        finally
        {
            waitRoute.Release();
        }

        var (settledReady, _) = await AdSpotJobTestHelpers.TryPollUntilAsync(
            client, waitSpotId, AdSpotJobTestHelpers.JobIsSettled, TimeSpan.FromSeconds(10));
        ProceededAfterInFlightCleared = settledReady;

        // ── AC6 — DELETE .../job cancels a job that is stuck mid-wait: 204, the row's job stamp
        // clears immediately (no polling needed — CancelAsync is awaited before DeleteJob answers),
        // and — the real proof the token itself was cancelled, not merely the row's stamp — no NEW
        // completion request ever arrives for this spot's own sponsor marker even once the station
        // clears afterward. ──
        var cancelSponsorId = await AdSpotJobTestHelpers.CreateSponsorAsync(client, CancelSponsorName);
        var cancelSpotId = await AdSpotJobTestHelpers.CreateDraftSpotAsync(client, cancelSponsorId, "Spot E", "A short deal that gets cancelled.");
        gate.Enter();
        int requestCountBaseline;
        try
        {
            var enqueue = await AdSpotJobTestHelpers.PostWriteAsync(client, cancelSpotId);
            if (enqueue.StatusCode != HttpStatusCode.Accepted)
                throw new InvalidOperationException($"arrange: POST /api/ads/{cancelSpotId}/write unexpectedly returned {enqueue.StatusCode}");

            // Whether DELETE below lands on a RUNNING job (parked mid-wait on the station) or a merely
            // QUEUED one decides which claim this scenario actually tests — captured, not discarded, so
            // TheRunningJobsTokenWasCancelled can assert it reached the running case it names.
            var (parked, _) = await AdSpotJobTestHelpers.TryPollUntilAsync(
                client, cancelSpotId, body => AdSpotJobTestHelpers.JobWaitingForStation(body) == true, TimeSpan.FromSeconds(5));
            CancelJobWasParkedOnTheStation = parked;

            requestCountBaseline = router.RequestCount;
            DeleteJobStatus = (await client.DeleteAsync($"/api/ads/{cancelSpotId}/job")).StatusCode;

            var afterCancel = await AdSpotJobTestHelpers.GetSpotAsync(client, cancelSpotId);
            JobIsNullRightAfterCancel = AdSpotJobTestHelpers.JobIsSettled(afterCancel) &&
                AdSpotJobTestHelpers.JobKind(afterCancel) is null && AdSpotJobTestHelpers.JobError(afterCancel) is null;
        }
        finally
        {
            gate.Exit();
        }

        // A positive signal AFTER the station cleared, not a fixed sleep — a DIFFERENT, never-cancelled
        // spot's own write is enqueued and awaited to settle; the single-reader consumer loop (AC1) means
        // it cannot even START until whatever ran before it (cancelSpotId's job, correctly cancelled or
        // not) has left the loop, so by the time it settles the cancelled job has had every chance it is
        // ever going to get to make its own unwanted completion call.
        var signalSponsorId = await AdSpotJobTestHelpers.CreateSponsorAsync(client, "Umber & Kestrel Mercantile");
        var signalSpotId = await AdSpotJobTestHelpers.CreateDraftSpotAsync(client, signalSponsorId, "Spot Signal", "A short deal used only as a timing signal.");
        var signalEnqueue = await AdSpotJobTestHelpers.PostWriteAsync(client, signalSpotId);
        if (signalEnqueue.StatusCode != HttpStatusCode.Accepted)
            throw new InvalidOperationException($"arrange: POST /api/ads/{signalSpotId}/write unexpectedly returned {signalEnqueue.StatusCode}");

        await AdSpotJobTestHelpers.TryPollUntilAsync(
            client, signalSpotId, AdSpotJobTestHelpers.JobIsSettled, TimeSpan.FromSeconds(10));
        NoNewRequestArrivedAfterCancel = router.RequestCount == requestCountBaseline + 1;

        // ── AC2 (sad path) — a second /write on the SAME spot, while the first is still stamped
        // (mid-wait on the station), is refused as 409 ad_job_busy. ──
        var busySponsorId = await AdSpotJobTestHelpers.CreateSponsorAsync(client, "Marrow & Loom Supply");
        var busySpotId = await AdSpotJobTestHelpers.CreateDraftSpotAsync(client, busySponsorId, "Spot F", "A short deal held busy.");
        gate.Enter();
        try
        {
            var first = await AdSpotJobTestHelpers.PostWriteAsync(client, busySpotId);
            if (first.StatusCode != HttpStatusCode.Accepted)
                throw new InvalidOperationException($"arrange: POST /api/ads/{busySpotId}/write unexpectedly returned {first.StatusCode}");

            var second = await AdSpotJobTestHelpers.PostWriteAsync(client, busySpotId);
            SecondWriteSameSpotStatus = second.StatusCode;
            var secondBody = await JsonDocument.ParseAsync(await second.Content.ReadAsStreamAsync());
            SecondWriteSameSpotType = secondBody.RootElement.TryGetProperty("type", out var type) ? type.GetString() : null;
        }
        finally
        {
            gate.Exit();
        }

        // No fact reads spot F's own settled state past this point — the wait below only keeps its
        // background write from racing this Arc's own teardown (see this class's remarks), so a stalled
        // write here cannot crash every other Scenario in this collection through a bare timeout; it
        // just leaves this discarded flag false.
        await AdSpotJobTestHelpers.TryPollUntilAsync(client, busySpotId, AdSpotJobTestHelpers.JobIsSettled, TimeSpan.FromSeconds(10));

        // ── 404 / idempotent 204 / 401 edges — /write and /job answer 404 for an id that names no
        // spot at all, DELETE /job is idempotently 204 (job stays null) on a spot that never had a
        // job, and a caller who never logged in gets 401 on both routes. ──
        WriteUnknownIdStatus = (await AdSpotJobTestHelpers.PostWriteAsync(client, 999_999)).StatusCode;
        DeleteJobUnknownIdStatus = (await client.DeleteAsync("/api/ads/999999/job")).StatusCode;

        var neverJobbedSponsorId = await AdSpotJobTestHelpers.CreateSponsorAsync(client, "Untouched & Sons");
        var neverJobbedSpotId = await AdSpotJobTestHelpers.CreateDraftSpotAsync(
            client, neverJobbedSponsorId, "Spot G", "Never given a job.");
        DeleteJobOnUnjobbedSpotStatus = (await client.DeleteAsync($"/api/ads/{neverJobbedSpotId}/job")).StatusCode;
        var afterIdempotentDelete = await AdSpotJobTestHelpers.GetSpotAsync(client, neverJobbedSpotId);
        JobIsNullAfterIdempotentDelete = !AdSpotJobTestHelpers.JobIsPresent(afterIdempotentDelete);

        var unauthenticatedClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        UnauthenticatedWriteStatus = (await AdSpotJobTestHelpers.PostWriteAsync(unauthenticatedClient, neverJobbedSpotId)).StatusCode;
        UnauthenticatedDeleteJobStatus = (await unauthenticatedClient.DeleteAsync($"/api/ads/{neverJobbedSpotId}/job")).StatusCode;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// Arranges STORY-422 AC3 alone — a station-wide job queue at exactly <c>Ads:JobQueueCapacity=1</c>,
/// so a THIRD enqueue while the FIRST already occupies the single running slot and the SECOND already
/// occupies the single queued slot is provably refused. A separate Arc/ephemeral Postgres from
/// <see cref="Story422Arc"/> (a <see cref="System.Threading.Channels.Channel{T}"/>'s own bound is fixed
/// at construction — one process boot, one capacity).
/// </summary>
public sealed class Story422QueueFullArc : IAsyncLifetime
{
    public HttpStatusCode FirstWriteStatus { get; private set; }
    public HttpStatusCode SecondWriteStatus { get; private set; }
    public HttpStatusCode ThirdWriteStatus { get; private set; }
    public string? ThirdWriteType { get; private set; }
    public bool ThirdSpotIsStamped { get; private set; }

    public async Task InitializeAsync()
    {
        await using var database = await Story422QueueFullDatabase.StartAsync();
        var router = new AdScriptCompletionsRouter();
        await using var factory = new Story422WebFactory(database, router, jobQueueCapacity: 1);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Story422WebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        var gate = factory.Services.GetRequiredService<OnAirRenderGate>();
        var sponsorId = await AdSpotJobTestHelpers.CreateSponsorAsync(client, "Quarrelsome Goose Bakery");
        var spot1Id = await AdSpotJobTestHelpers.CreateDraftSpotAsync(client, sponsorId, "Spot 1", "A short deal, first in line.");
        var spot2Id = await AdSpotJobTestHelpers.CreateDraftSpotAsync(client, sponsorId, "Spot 2", "A short deal, second in line.");
        var spot3Id = await AdSpotJobTestHelpers.CreateDraftSpotAsync(client, sponsorId, "Spot 3", "A short deal that never fits.");

        // Occupying the running slot for the rest of this Arc's own lifetime (never released — this
        // Arc exists to prove AC3 alone, so nothing here ever needs to actually complete a write).
        gate.Enter();

        FirstWriteStatus = (await AdSpotJobTestHelpers.PostWriteAsync(client, spot1Id)).StatusCode;

        // Spot 1 must actually be DEQUEUED (the channel buffer now empty) before Spot 2's own enqueue
        // can prove anything about the buffer's single slot — a bounded wait rather than a throwing one,
        // so a station that never actually waits still lets this Arc run to its own assertions instead
        // of crashing arrangement outright.
        await AdSpotJobTestHelpers.TryPollUntilAsync(
            client, spot1Id, body => AdSpotJobTestHelpers.JobWaitingForStation(body) == true, TimeSpan.FromSeconds(5));

        SecondWriteStatus = (await AdSpotJobTestHelpers.PostWriteAsync(client, spot2Id)).StatusCode;

        var third = await AdSpotJobTestHelpers.PostWriteAsync(client, spot3Id);
        ThirdWriteStatus = third.StatusCode;
        var thirdBody = await JsonDocument.ParseAsync(await third.Content.ReadAsStreamAsync());
        ThirdWriteType = thirdBody.RootElement.TryGetProperty("type", out var type) ? type.GetString() : null;

        var thirdSpot = await AdSpotJobTestHelpers.GetSpotAsync(client, spot3Id);
        ThirdSpotIsStamped = AdSpotJobTestHelpers.JobIsPresent(thirdSpot);

        // Cleanup — cancels the running (spot1) and queued (spot2) jobs so nothing lingers past this
        // Arc's own teardown; spot3 was refused outright, so it never carries a stamp to clear.
        await client.DeleteAsync($"/api/ads/{spot1Id}/job");
        await client.DeleteAsync($"/api/ads/{spot2Id}/job");
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harness — WebApplicationFactory + ephemeral Postgres subclasses (the Story412WebFactory/
// Story412Database "`file`-scoped types cannot cross files" precedent — this file supplies its own,
// shared by both Arcs above since only the queue capacity and the underlying database differ). ──

file sealed class Story422WebFactory(EphemeralStationDatabase db, AdScriptCompletionsRouter router, int jobQueueCapacity)
    : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t441-job-runner";

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
        builder.UseSetting("Ads:JobQueueCapacity", jobQueueCapacity.ToString());
        builder.UseSetting("Llm:Endpoint", "http://fake-llm.local");
        builder.UseSetting("Llm:Model", "test-model");

        builder.ConfigureTestServices(services =>
        {
            // Strips every hosted service, THEN re-adds only AdSpotJobService (the Story412Arc
            // precedent, extended) — this suite is the one place in this test project that genuinely
            // needs its own hosted service alive: the write job it tests IS a hosted service.
            services.RemoveAll<IHostedService>();
            services.AddHostedService(sp => sp.GetRequiredService<AdSpotJobService>());

            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(router.Handler));
        });
    }
}

file sealed class Story422Database : EphemeralStationDatabase
{
    Story422Database(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story422Database> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t441a");
        var db = new Story422Database(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

file sealed class Story422QueueFullDatabase : EphemeralStationDatabase
{
    Story422QueueFullDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story422QueueFullDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t441b");
        var db = new Story422QueueFullDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}
