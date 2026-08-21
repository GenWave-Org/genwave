// STORY-353 — A red LLM tile names its cause (SPEC F139 · PLAN T330/T334/T335)
//
// BDD specification — xUnit. T330's facts below drive the REAL resolution paths — a hand-rolled
// LlmCopyWriter (the house FakeHttpMessageHandler/SingleHandlerHttpClientFactory idiom, mirrors
// GenWave.Tts.Tests' Story189_LlmSingleFlightAndWarnDetail) for the Copy-kind causes, and the shared
// CrosstalkWorkerHarness (Support/) — the SAME real CrosstalkStockWorker/CrosstalkScriptWriter/
// CrosstalkAssembler wiring Story328_CrosstalkStockWorker.cs itself drives — for the break-window
// abandon. ScenarioCountersRoll is the one PURE-level exception (LlmCallCauseCounters has no I/O of
// its own to drive through). T334-tagged (the /api/llm-calls surface) and T331-tagged (TruthGateReject)
// facts stay pending — see docs/PLAN.md.
//
// gh-#365's acceptance is the dev-station case verbatim: a tile that flaps red every 1–2 hours on an
// external ollama (gemma-class on a 16GB 4090 laptop) explains itself from the admin UI — no SSH, no
// Loki, no darts at Llm settings.

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using GenWave.Core.Domain;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

public static class FeatureLlmCauseTaxonomy
{
    // ── Shared arrange for the Copy-kind facts (mirrors GenWave.Tts.Tests' own BuildWriter idiom) ──

    static SegmentRequest LeadInRequest() =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    /// <summary>Builds a REAL <see cref="LlmCopyWriter"/> against a fake completions handler — the
    /// one constructor arg list every fact in <see cref="ScenarioOutcomesAreTyped"/> shares (except
    /// the window-cancel fact, which uses <see cref="CrosstalkWorkerHarness"/> instead). Hands back
    /// the ring AND the counters (SPEC F139 review finding F2) — the SAME <see cref="LlmCallRecorder"/>
    /// feeds both, so a fact can prove either half moved, or both.</summary>
    static (LlmCopyWriter Writer, LlmCallRing Ring, LlmCallCauseCounters Counters) BuildWriter(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        int timeoutSeconds = 5, int maxCopyChars = 450)
    {
        var ring = new LlmCallRing(new FakeOptionsMonitor<LlmOptions>(new LlmOptions()));
        var counters = new LlmCallCauseCounters(TimeProvider.System);
        var writer = new LlmCopyWriter(
            new TemplateCopyWriter(new PatterTemplateRenderer()),
            new SingleHandlerHttpClientFactory(new FakeHttpMessageHandler(respond)),
            new FakeOptionsMonitor<LlmOptions>(new LlmOptions
            {
                Endpoint = "http://fake-llm.local", Model = "test-model", TimeoutSeconds = timeoutSeconds,
                MaxCopyChars = maxCopyChars,
            }),
            new LlmCopyStatusHolder(),
            new FakeActivePersonaAccessor(),
            NullLogger<LlmCopyWriter>.Instance,
            TimeProvider.System,
            new LlmCallRecorder(ring, counters),
            new FakeDegradationModeReader());
        return (writer, ring, counters);
    }

    static Task<HttpResponseMessage> Ok(string content) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = CompletionsBody(content),
    });

    static StringContent CompletionsBody(string content) => new(
        JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content } } },
        }),
        Encoding.UTF8, "application/json");

    public static class ScenarioOutcomesAreTyped
    {
        [Fact]
        public static async Task A_successful_call_records_Success()
        {
            // Given a completions reply that fits comfortably under Llm:MaxCopyChars...
            var (writer, ring, _) = BuildWriter((_, _) => Ok("Great tune coming up, stay tuned."));

            // When it airs through the real WriteAsync -> RequestCleanedCompletionAsync seam...
            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            // Then the F73 ring entry it left behind carries exactly one cause: Success.
            Assert.Equal(LlmCallCause.Success, Assert.Single(ring.Snapshot()).Cause);
        }

        [Fact]
        public static async Task A_timed_out_call_records_Timeout()
        {
            // Given a completions endpoint that never answers inside Llm:TimeoutSeconds...
            var (writer, ring, _) = BuildWriter(
                async (_, ct) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                },
                timeoutSeconds: 1);

            // When the render's own timeout budget elapses (RequestCleanedCompletionAsync's own
            // timeoutCts, not the caller's token — CancellationToken.None here proves that)...
            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            // Then the ring records Timeout — never a generic connection failure.
            Assert.Equal(LlmCallCause.Timeout, Assert.Single(ring.Snapshot()).Cause);
        }

        [Fact]
        public static async Task An_over_length_call_records_OverLength()
        {
            // Given a reply with no sentence terminator anywhere, well over a tiny Llm:MaxCopyChars —
            // the gh-#277 shape: nothing survives TrimToLastCompleteSentence's own salvage, so CleanCopy
            // rejects with WasOverLength: true (a candidate existed, none fit).
            var overLengthNoTerminator = string.Concat(Enumerable.Repeat("word ", 40));
            var (writer, ring, _) = BuildWriter((_, _) => Ok(overLengthNoTerminator), maxCopyChars: 50);

            // When it resolves through the real MaxCopyChars rejection path...
            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            // Then the ring names the gh-#277 family by its new name: OverLength.
            Assert.Equal(LlmCallCause.OverLength, Assert.Single(ring.Snapshot()).Cause);
        }

        // SPEC F139 review finding F2 (T330): mutation-proven, the Story326:430 precedent one file
        // over — LlmCallRecorder folds the ring write and the counter write into ONE call, so there
        // is no longer a "delete just the counter half" mutation to express; this fact still pins the
        // counter side explicitly rather than trusting that structural guarantee alone.
        [Fact]
        public static async Task A_successful_call_moves_the_cause_counter()
        {
            // Given a REAL writer wired to a REAL LlmCallRecorder (ring + counters, one call)...
            var (writer, _, counters) = BuildWriter((_, _) => Ok("Great tune coming up, stay tuned."));

            // When it airs through the real WriteAsync -> RequestCleanedCompletionAsync seam...
            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            // Then the rolling cause counter moved too — not just the ring.
            Assert.Equal(
                1, counters.Snapshot().Single(row =>
                    row is { Cause: LlmCallCause.Success, Model: "test-model", Kind: LlmCallKind.Copy }).Count);
        }

        /// <summary>Drives the REAL <c>CrosstalkStockWorker</c>/<c>CrosstalkScriptWriter</c>/
        /// <c>CrosstalkAssembler</c> wiring via <see cref="CrosstalkWorkerHarness"/> — the SAME
        /// arrangement <c>Story328_CrosstalkStockWorker.An_in_flight_generation_is_cancelled_the_instant_the_window_reopens</c>
        /// drives, supplying this fact's OWN <see cref="LlmCallRing"/> so it can read back what the
        /// worker stamped (SPEC F139.1's own "reuse the signal, don't re-derive" — CanceledByWindow is
        /// stamped by the worker, never by CrosstalkScriptWriter itself; see
        /// <c>CrosstalkStockWorker.RecordWindowCancellation</c>'s own remarks for why).</summary>
        [Fact]
        public static async Task A_window_cancelled_stock_call_records_CanceledByWindow()
        {
            const string ShowSlug = "night-shift";
            const string ShowName = "Night Shift";

            // Given a real stock-timer tick whose script generation completes (an Accepted exchange —
            // its own ring entry lands as Success) but whose per-line synth blocks forever...
            var ring = new LlmCallRing(new FakeOptionsMonitor<LlmOptions>(new LlmOptions()));
            var now = new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero); // a Monday noon
            var (worker, gate, timeProvider, _, llmHandler, synthesizer) =
                await CrosstalkWorkerHarness.BuildAsync(now, ShowSlug, ShowName, callRing: ring);

            var tickTask = worker.TickOnceAsync(CancellationToken.None);

            // ...and generation genuinely started (the positive control: the LLM was really called).
            await synthesizer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(llmHandler.Requests);

            // When a real on-air render starts mid-flight and the watchdog's next poll observes it...
            gate.Enter();
            timeProvider.Advance(TimeSpan.FromSeconds(3)); // CrosstalkStockWorker's own WatchdogInterval
            await tickTask.WaitAsync(TimeSpan.FromSeconds(5));

            // Then the NEWEST ring entry (the abandoned synth, recorded after the earlier successful
            // script generation) carries CanceledByWindow, under the Crosstalk kind.
            var newest = ring.Snapshot()[0];
            Assert.Equal(LlmCallCause.CanceledByWindow, newest.Cause);
            Assert.Equal(LlmCallKind.Crosstalk, newest.Kind);
        }
    }

    // ── PURE level: LlmCallCauseCounters has no I/O of its own to drive through a resolution path ──

    public static class ScenarioCountersRoll
    {
        [Fact]
        public static void Counts_group_per_cause_model_and_kind()
        {
            // Given a mix of resolved calls across two models and both segment kinds...
            var counters = new LlmCallCauseCounters(new FakeTimeProvider(DateTimeOffset.UtcNow));
            counters.Record(LlmCallCause.Success, "model-a", LlmCallKind.Copy);
            counters.Record(LlmCallCause.Success, "model-a", LlmCallKind.Copy);
            counters.Record(LlmCallCause.Timeout, "model-a", LlmCallKind.Copy);
            counters.Record(LlmCallCause.Success, "model-b", LlmCallKind.Crosstalk);

            // When the rolling 24h counters are read...
            var snapshot = counters.Snapshot();

            // Then counts are grouped per (cause, model, kind) — never merged across a differing key.
            Assert.Equal(2, snapshot.Single(row => row is { Cause: LlmCallCause.Success, Model: "model-a", Kind: LlmCallKind.Copy }).Count);
            Assert.Equal(1, snapshot.Single(row => row is { Cause: LlmCallCause.Timeout, Model: "model-a", Kind: LlmCallKind.Copy }).Count);
            Assert.Equal(1, snapshot.Single(row => row is { Cause: LlmCallCause.Success, Model: "model-b", Kind: LlmCallKind.Crosstalk }).Count);
        }

        // Renamed (STORY-353 AC2, amended at T330 review) from Entries_older_than_24h_stop_counting —
        // that name asserted a false 24h razor's edge the class body immediately below contradicts.
        // The true, honest claim: the hourly-bucket band ages entries out somewhere between 24h and
        // 25h, never under, so a 25h advance is the worst-case proof this window forgot the entry.
        [Fact]
        public static void Entries_age_out_on_the_hourly_bucket_band()
        {
            // Given one recorded call, counted at the START of its own hourly bucket (an
            // hour-aligned clock, so the 25h advance below unambiguously clears LlmCallCauseCounters'
            // own bucket-granularity slop — see that class's own remarks: a bucket can hold entries up
            // to just under an hour newer than its own start, so the true retention window is "24h to
            // 25h", never a razor's-edge 24h+1min)...
            var hourAligned = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var timeProvider = new FakeTimeProvider(hourAligned);
            var counters = new LlmCallCauseCounters(timeProvider);
            counters.Record(LlmCallCause.Timeout, "model-a", LlmCallKind.Copy);
            Assert.Single(counters.Snapshot());

            // When the rolling window's own clock (TimeProvider, never wall-clock) advances past the
            // full 25h worst case...
            timeProvider.Advance(TimeSpan.FromHours(25));

            // Then it no longer counts — the window forgot it.
            Assert.Empty(counters.Snapshot());
        }
    }

    public static class ScenarioTheSurfaceServesTheTaxonomy
    {
        // The deployed entry point: /api/llm-calls through WebApplicationFactory<Program>.
        [Fact(Skip = "pending T334 — surface not extended yet")]
        public static void Each_call_row_carries_its_cause() =>
            Assert.Fail("pending T334: a real request through the production pipeline shows the cause per call");

        [Fact(Skip = "pending T334")]
        public static void The_counter_summary_rides_the_response() =>
            Assert.Fail("pending T334: the 24h by-cause summary is served alongside the ring");
    }

    public static class SadPathDiscipline
    {
        [Fact]
        public static async Task Nothing_survives_a_restart()
        {
            // Given a ring entry and a counter recorded on one process...
            var (writer, ring, _) = BuildWriter((_, _) => Ok("Great tune coming up, stay tuned."));
            await writer.WriteAsync(LeadInRequest(), CancellationToken.None);
            Assert.Single(ring.Snapshot());

            var counters = new LlmCallCauseCounters(TimeProvider.System);
            counters.Record(LlmCallCause.Success, "test-model", LlmCallKind.Copy);
            Assert.Single(counters.Snapshot());

            // When a brand-new ring and counter store stand up — nothing about either type persists
            // anything (F73.3/F139.3 stand): both constructors' only dependency is an options monitor
            // or a TimeProvider, no store/repository/connection type in sight — a fresh instance is
            // the strongest available proof at this level, mirroring Story196's own AC3 idiom.
            var freshRing = new LlmCallRing(new FakeOptionsMonitor<LlmOptions>(new LlmOptions()));
            var freshCounters = new LlmCallCauseCounters(TimeProvider.System);

            // Then both start empty.
            Assert.Empty(freshRing.Snapshot());
            Assert.Empty(freshCounters.Snapshot());
        }

        [Fact]
        public static async Task A_truth_gate_rejection_is_its_own_cause()
        {
            // Given a ContextSegment render whose first reply fabricates a claim the real fact
            // block never supports (the gh-#434 exhibit shape), and a re-ask reply that finally
            // supports it — driven through the real F138.2 gate at the LlmCopyWriter seam (PLAN T331)
            const string factBlock = "Edmonton: overcast, 15°C. Today's high 21°C, low 12°C.";
            const string poisonedCopy =
                "It feels like 6 degrees below freezing with plenty of sunshine and today is saturday here in the studio.";
            const string cleanCopy = "It's overcast today at 15 degrees with a high of 21 and a low of 12.";
            var callCount = 0;
            var (writer, ring, _) = BuildWriter((_, _) =>
            {
                callCount++;
                return Ok(callCount == 1 ? poisonedCopy : cleanCopy);
            });
            var request = new SegmentRequest(
                SegmentKind.ContextSegment, "af_heart", "GenWave", Track: null, DateTimeOffset.UtcNow,
                "test-station", PersonaName: null, CounterpartName: null, ContextFacts: factBlock);

            // When it renders
            await writer.WriteAsync(request, CancellationToken.None);

            // Then the ring carries BOTH calls, and the rejected first one is stamped
            // TruthGateReject — its own cause, distinct from the re-ask's own Success.
            var records = ring.Snapshot();
            Assert.Equal(2, records.Count);
            Assert.Contains(records, record => record.Cause == LlmCallCause.TruthGateReject);
            Assert.Contains(records, record => record.Cause == LlmCallCause.Success);
        }
    }
}
