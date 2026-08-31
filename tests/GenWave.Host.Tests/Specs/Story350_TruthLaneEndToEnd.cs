// STORY-350, STORY-351, STORY-353 — the truth lane wired end to end (SPEC F138-F139 · PLAN T335)
//
// BDD specification — xUnit. Every fact below drives the REAL production DI graph
// (WebApplicationFactory<Program>, via the shared Support/LlmCompletionsStub.cs types T334
// extracted) rather than a hand-built collaborator graph — the wire-proof acceptance PLAN T335
// itself states: "real Kestrel, real render chain, real admin UI."
//
// ALTITUDE (read this before any fact below — every render seam here is chosen deliberately, not
// by default):
//
//   * ScenarioContextSegmentReasksThenAirsTheCleanReply and
//     ScenarioLeadInWrongWeekdayDegradesAndTheOperatorCanSeeWhy resolve the REAL, singleton
//     ISegmentCopyWriter straight off the booted container (factory.Services.GetRequiredService)
//     and call WriteAsync directly, rather than reaching it over HTTP. There is no HTTP surface
//     that reaches it WITH the facts these scenarios need: PersonaController.Preview is bound
//     straight to LlmCopyWriter (bypassing DegradationGatedCopyWriter entirely, SPEC F69.4) and
//     never carries ContextFacts at all (PLAN T331's own "structurally ungated" finding) — the
//     ONLY caller that ever builds a fact-bearing ContextSegment/LeadIn SegmentRequest is the
//     Orchestrator's own background playout loop, which LlmCompletionsWebFactory removes along
//     with every other IHostedService (no Liquidsoap/Postgres churn during a test). Resolving the
//     interface straight off the container is therefore the HIGHEST seam honestly reachable: every
//     collaborator downstream of it — the named HttpClient, DegradationController, and critically
//     the LlmCallRing/LlmCallCauseCounters singletons — is the exact SAME production object
//     GET /api/llm-calls and GET /api/status read moments later over real authenticated requests.
//
//   * ScenarioCrosstalkTruthDiscardIsVisibleOnTheSurface resolves the real CrosstalkScriptWriter
//     singleton the SAME way (it is an ordinary AddSingleton<CrosstalkScriptWriter>() in
//     TtsServiceCollectionExtensions, reachable directly — no CrosstalkWorkerHarness detour
//     needed: that harness hand-builds its OWN isolated LlmCallRing/counters pair unless a caller
//     explicitly threads the production ones through it, so resolving the real DI singleton
//     directly is the MORE honest seam here, not a fallback from it) and calls
//     WriteExchangeAsync directly, against the SAME stub.
//
// Every scenario shares the SAME Support/LlmCompletionsStub.cs types Story196_LlmCallInspector.cs
// and Story353_LlmCauseTaxonomy.cs already use — extended minimally (T335) with call-sequenced
// QueueReplies and captured Requests, additively, so neither existing file changed.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host;
using GenWave.Host.Tests;
using GenWave.Host.Tests.Support;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

// ── Wire shapes (mirrors Story353_LlmCauseTaxonomy.cs's own narrower-than-the-DTO idiom — a
// `file`-scoped redefinition per spec file, never a cross-file reference to the server DTOs) ──────

/// <summary>Wire shape of one row from <c>GET /api/llm-calls</c> (SPEC F73.1, F139.1) — adds
/// <c>Seq</c> over Story353's own narrower row so a fact can order the ring's newest-first response
/// back into call order.</summary>
file sealed record LlmCallRow(long Seq, string Cause, string Model, string Kind);

/// <summary>Wire shape of one <c>causeSummary</c> row (SPEC F139.2).</summary>
file sealed record LlmCallCauseSummaryRow(string Cause, string Model, string Kind, int Count);

/// <summary>Wire shape of <c>GET /api/llm-calls</c> itself (SPEC F139.2).</summary>
file sealed record LlmCallsSurfaceResponse(
    IReadOnlyList<LlmCallRow> Calls, IReadOnlyList<LlmCallCauseSummaryRow> CauseSummary);

/// <summary>Wire shape of the <c>llm</c> block on <c>GET /api/status</c> (SPEC F34.8, F139.2) — only
/// the three F139.2 dominant-cause fields this file asserts on.</summary>
file sealed record StatusLlmBlock(string? DominantCause, int? DominantCauseCount, string? DominantCauseModel);

/// <summary>Wire shape of <c>GET /api/status</c> itself — only the <c>llm</c> block; every other
/// top-level field (<c>catalog</c>, <c>safeScope</c>, …) is simply never bound.</summary>
file sealed record StatusSurfaceResponse(StatusLlmBlock Llm);

/// <summary>
/// The SAME production wiring <c>LlmCompletionsWebFactory</c> (Support/LlmCompletionsStub.cs)
/// configures — that type is <c>sealed</c>, so this is composition-by-duplication, not inheritance,
/// mirroring this test project's own "redefine, don't reach across" convention that Support file's
/// own header note documents — PLUS a fake <see cref="IMediaCatalog"/>. <c>GET /api/status</c>'s
/// <c>StatusController</c> resolves the real Postgres-backed <c>MediaRepository</c> otherwise, which
/// does NOT share <c>OnAirPersonaAccessor</c>'s/<c>CachingScheduleResolver</c>'s own graceful
/// "unconfigured Station Postgres is a supported deployment shape" contract — it throws against this
/// factory's bogus <c>ConnectionStrings:Library</c>, 500ing the one scenario below that reads
/// <c>/api/status</c>. The shared, already-built <c>tests/GenWave.Host.Tests/FakeMediaCatalog.cs</c>
/// (STORY-084's own "for GET /api/status specs" fake) is the right tool here — no new fake invented
/// for this file. PLAN T371 (SPEC F149.5) adds a SECOND Postgres-backed dependency
/// <c>StatusController</c> resolves, <c>IMediaRotationSink</c> — faked the identical way, via the
/// shared <c>tests/GenWave.Host.Tests/FakeMediaRotationSink.cs</c>.
/// </summary>
file sealed class TruthLaneStatusWebFactory(string llmEndpoint) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", LlmCompletionsWebFactory.Password);
        builder.UseSetting("Llm:Endpoint", llmEndpoint);
        builder.UseSetting("Llm:Model", LlmCompletionsWebFactory.Model);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IMediaRotationSink>();
            services.AddSingleton<IMediaRotationSink>(new FakeMediaRotationSink());
        });
    }
}

public static class FeatureTruthLaneEndToEnd
{
    static async Task LoginAsync(HttpClient client, string password)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    /// <summary>A minimal, valid two-persona pair for <see cref="CrosstalkExchangeRequest"/> — mirrors
    /// Support/CrosstalkWorkerHarness.cs's own identically-named helper (redefined here rather than
    /// shared: that harness's helper is a `file`-scoped method one project idiom over, and this fact
    /// needs no other part of that harness).</summary>
    static PersonaCard MakeCard(string name) =>
        new(1, name, "", $"{name}'s soul.", [], new VoiceSpec("kokoro", "af_heart", 1.0, "en"),
            EnergyDisposition: 0, [], []);

    public static class ScenarioContextSegmentReasksThenAirsTheCleanReply
    {
        const string FactBlock = "Calgary: sunny, 18°C. Wind 10 km/h from the northwest.";
        const string PoisonedCopy = "It's a blustery 45 degrees out there today.";
        const string CleanCopy = "It's sunny at 18 degrees with wind at 10 kilometers per hour from the northwest.";

        [Fact]
        public static async Task A_poisoned_digit_reasks_once_and_the_endpoint_shows_both_causes()
        {
            // Given a real production host wired to a real (stub) completions endpoint whose FIRST
            // reply claims a digit (45) the fact block never supports, and whose re-ask reply is clean...
            await using var stub = await LlmCompletionsStub.StartAsync();
            stub.QueueReplies(PoisonedCopy, CleanCopy);
            await using var factory = new LlmCompletionsWebFactory(stub.BaseUri.ToString());
            var client = factory.CreateClient();
            await LoginAsync(client, LlmCompletionsWebFactory.Password);

            var writer = factory.Services.GetRequiredService<ISegmentCopyWriter>();
            var request = new SegmentRequest(
                SegmentKind.ContextSegment, "af_heart", "GenWave", Track: null, DateTimeOffset.UtcNow,
                "test-station", PersonaName: null, CounterpartName: null, ContextFacts: FactBlock);

            // When it renders through the REAL DegradationGatedCopyWriter -> LlmCopyWriter chain
            // the Orchestrator's own graph resolves (SPEC F34.1, F69.1)...
            var result = await writer.WriteAsync(request, CancellationToken.None);

            // Then the gate re-asked exactly once, and the clean reply is what airs.
            Assert.Equal(2, stub.Requests.Count);
            Assert.Equal(CleanCopy, result.Text);
            Assert.True(result.FreshPerAiring);

            // And the F138.5 guard line rode BOTH system prompts, read back off the real wire body
            // the stub actually received — not merely asserted against in-process state.
            Assert.All(
                stub.Requests,
                req => Assert.Contains("Never name another day or time of day.", req.SystemPrompt));

            // When the real admin endpoint is read back over an authenticated request...
            var response = await client.GetFromJsonAsync<LlmCallsSurfaceResponse>("/api/llm-calls");
            Assert.NotNull(response);
            var ordered = response!.Calls.OrderBy(row => row.Seq).ToArray();

            // Then the first (rejected) call and the second (accepted re-ask) each carry their own
            // honest cause — never folded into one entry.
            Assert.Equal(2, ordered.Length);
            Assert.Equal("truthgatereject", ordered[0].Cause);
            Assert.Equal("success", ordered[1].Cause);
            Assert.All(ordered, row => Assert.Equal(LlmCompletionsWebFactory.Model, row.Model));

            // ...and the 24h causeSummary counts both, per (cause, model, kind).
            Assert.Contains(
                response.CauseSummary,
                row => row is { Cause: "truthgatereject", Model: LlmCompletionsWebFactory.Model, Kind: "copy", Count: 1 });
            Assert.Contains(
                response.CauseSummary,
                row => row is { Cause: "success", Model: LlmCompletionsWebFactory.Model, Kind: "copy", Count: 1 });
        }
    }

    public static class ScenarioLeadInWrongWeekdayDegradesAndTheOperatorCanSeeWhy
    {
        [Fact]
        public static async Task Both_replies_claiming_the_wrong_weekday_degrade_to_the_template_and_the_wire_names_the_cause()
        {
            // Given a real production host, and a wrong-weekday claim computed against the SAME real
            // IStationClockProvider seam the writer itself reads (never a fixed fixture date) — so
            // this fact is correct regardless which day it actually runs on...
            await using var stub = await LlmCompletionsStub.StartAsync();
            await using var factory = new TruthLaneStatusWebFactory(stub.BaseUri.ToString());
            var client = factory.CreateClient();
            await LoginAsync(client, LlmCompletionsWebFactory.Password);

            var stationClock = factory.Services.GetRequiredService<IStationClockProvider>();
            var actualWeekday = stationClock.LocalNow.DayOfWeek;
            var wrongWeekday = actualWeekday == DayOfWeek.Saturday ? DayOfWeek.Sunday : DayOfWeek.Saturday;
            // ReplyContent (not QueueReplies): BOTH the first call and the re-ask must claim the
            // SAME wrong weekday, so the ladder's re-ask still violates and the render exhausts it.
            stub.ReplyContent = $"This {wrongWeekday} has been one for the books so let's keep it going.";

            var writer = factory.Services.GetRequiredService<ISegmentCopyWriter>();
            var request = new SegmentRequest(
                SegmentKind.LeadIn, "af_heart", "GenWave",
                new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
                DateTimeOffset.UtcNow, "test-station");

            // When it renders through the real writer chain and the re-ask still violates...
            var result = await writer.WriteAsync(request, CancellationToken.None);

            // Then it degrades to the deterministic LeadIn template floor — never the still-violating
            // LLM text, and never silence (LeadIn carries no F107.6-style skip guard).
            Assert.Equal("Coming up: Astral Plane by Valerie June.", result.Text);
            Assert.False(result.FreshPerAiring);
            Assert.Equal(2, stub.Requests.Count);
            Assert.All(
                stub.Requests,
                req => Assert.Contains("Never name another day or time of day.", req.SystemPrompt));

            // And the real admin endpoint shows both rejections in its ring.
            var callsResponse = await client.GetFromJsonAsync<LlmCallsSurfaceResponse>("/api/llm-calls");
            Assert.NotNull(callsResponse);
            Assert.Equal(2, callsResponse!.Calls.Count(row => row.Cause == "truthgatereject"));

            // And, after this render, GET /api/status names the SAME dominant cause + model an
            // operator staring at a red LLM tile needs (SPEC F139.2) — no SSH, no Loki.
            var status = await client.GetFromJsonAsync<StatusSurfaceResponse>("/api/status");
            Assert.NotNull(status);
            Assert.Equal("truthgatereject", status!.Llm.DominantCause);
            Assert.True(status.Llm.DominantCauseCount >= 2);
            Assert.Equal(LlmCompletionsWebFactory.Model, status.Llm.DominantCauseModel);
        }
    }

    public static class ScenarioCrosstalkTruthDiscardIsVisibleOnTheSurface
    {
        // Clears every SHAPE rule (3-8 alternating HOST:/NEIGHBOR: lines, both speakers present, no
        // line over budget) but names a real-world FM frequency (SPEC F138.6) — never a weekday/
        // daypart/condition/date word, so this fact's outcome depends on nothing but the frequency
        // shape, regardless of which TruthShapeChecks entry happens to run first.
        const string FrequencyScript =
            "HOST: Hey glad you could drop by the studio for a chat.\n" +
            "NEIGHBOR: Always fun swinging by between tracks.\n" +
            "HOST: Someone in the chat says we sound just like 101 FM.\n" +
            "NEIGHBOR: Ha well we will take that as a compliment and keep the music going.";

        [Fact]
        public static async Task A_real_world_frequency_discards_the_exchange_and_the_summary_names_the_crosstalk_lane()
        {
            // Given a real production host, and a reply naming a real-world FM frequency...
            await using var stub = await LlmCompletionsStub.StartAsync();
            stub.ReplyContent = FrequencyScript;
            await using var factory = new LlmCompletionsWebFactory(stub.BaseUri.ToString());
            var client = factory.CreateClient();
            await LoginAsync(client, LlmCompletionsWebFactory.Password);

            // The real CrosstalkScriptWriter singleton (TtsServiceCollectionExtensions'
            // AddSingleton<CrosstalkScriptWriter>()) — reachable directly, no worker/harness needed.
            var scriptWriter = factory.Services.GetRequiredService<CrosstalkScriptWriter>();
            var request = new CrosstalkExchangeRequest(
                MakeCard("Host DJ"), MakeCard("Next DJ"), "GenWave", ShowName: null, Daypart: null,
                StationLocalNow: DateTimeOffset.UtcNow);

            // When it renders through the real writer...
            var result = await scriptWriter.WriteExchangeAsync(request, CancellationToken.None);

            // Then the exchange is discarded on the real F138.6 truth check — never a re-ask (F127.4
            // has none for crosstalk: a truth discard is silent, the stock worker just tries again).
            var discarded = Assert.IsType<CrosstalkWriteResult.Discarded>(result);
            Assert.Equal(LlmCallCause.TruthGateReject, discarded.Cause);
            Assert.Single(stub.Requests);
            Assert.Contains("Never name another day or time of day.", stub.Requests[0].SystemPrompt);

            // And the real admin endpoint's causeSummary carries kind=crosstalk for this discard —
            // the T334 review's own open line item: the crosstalk lane's 24h aggregate is on the wire
            // but was rendered nowhere until this proof.
            var response = await client.GetFromJsonAsync<LlmCallsSurfaceResponse>("/api/llm-calls");
            Assert.NotNull(response);
            Assert.Contains(
                response!.CauseSummary,
                row => row is { Cause: "truthgatereject", Kind: "crosstalk", Count: 1 });
        }
    }
}
