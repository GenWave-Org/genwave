// STORY-357..361 — PA mode end to end (SPEC F143-F146 · PLAN T345) — THE SLICE GATE.
//
// BDD specification — xUnit. The production DI graph (WebApplicationFactory<Program>, via a real
// ephemeral Postgres carrying every db/ migration — see TestStationDatabase's own remarks) — the
// SAME "real Kestrel, real render chain, real admin UI" acceptance PLAN T335 itself established for
// the truth lane (Story350_TruthLaneEndToEnd.cs), applied here to the whole PA-mode slice. Every
// scenario below is its own self-contained arc: its OWN ephemeral Postgres, its OWN
// WebApplicationFactory, arranged exactly ONCE inside an IAsyncLifetime fixture shared across that
// scenario's Facts via IClassFixture<T> — xUnit constructs a fresh test-class instance per [Fact], but
// IClassFixture<T> guarantees the FIXTURE (where the real arrange/act lives) is built once and reused,
// so "arrange once per scenario, one assertion per fact" holds literally rather than by convention only.
//
// ALTITUDE — what "air" honestly means at each seam (read before any fact below):
//
//   * VEND: the real, container-resolved IAnnouncementSource (SpectatorModeAnnouncementVendGuard
//     decorating AnnouncementRepository, both against the real ephemeral Postgres) — ClaimDeliverableAsync
//     called directly. There is no HTTP surface for the vend step (it lives inside the Orchestrator's
//     own background playout loop, which this file's WebApplicationFactory removes along with every
//     other IHostedService, exactly as Story358_AnnouncementFlavorEndToEnd.cs's own header already
//     establishes) — resolving the interface straight off the container is the highest honestly
//     reachable seam, the T335 ISegmentCopyWriter-off-the-container precedent applied one project over.
//
//   * FLAVOR: the real, container-resolved IAnnouncementCopyWriter (LlmCopyWriter itself — the SAME
//     production writer instance the on-air announcement dispatch would use), driven against a real
//     Kestrel-backed completions stub for "healthy" and a genuinely unreachable loopback port for
//     "fenced" — Story358's own precedent, extended here past copy into a real render.
//
//   * RENDER: the real, container-resolved IVerbatimSegmentRenderer (TtsSegmentSource) — the SAME
//     cache/loudness/cue/blurb-dir pipeline the feeder uses, run against a REAL ffmpeg (loudness/cue
//     analysis) on REAL audio bytes. Kokoro itself has no reachable port under the bench freeze, so the
//     ONE fake in this entire chain is a Kestrel-backed stub standing in for kokoro-fastapi's
//     POST /v1/audio/speech (KokoroSpeechStub below) — it returns a real, non-zero-duration WAV ffmpeg
//     can genuinely probe/analyze, mirroring GenWave.Tts.Tests.Fakes.FakeCrosstalkVoiceSynthesizer's own
//     "real bytes, not a zero-sample stand-in" reasoning one layer up (an HTTP stub rather than a
//     synthesizer substitution, since NormalizingTtsSynthesizer's own real correction/pronunciation pass
//     sits between TtsSegmentSource and the network hop and must stay genuine production code, never
//     bypassed). "The rendered wav's copy IS the exact message" is checked the same honest way
//     Story350_TruthLaneEndToEnd.cs already checks a system prompt rode the real wire: read back the
//     stub's own CAPTURED request body, never decoded audio (TTS text cannot be recovered from a
//     waveform) and never in-process state.
//
//   * AIR: the genuine TrackAired publish through the REAL, fully-composed CompositeStationEventSink
//     (T343's own machinery, Story343_AnnouncementLifecycleSmoke.cs's own precedent) — never a direct
//     IAnnouncementLifecycle.MarkAiredAsync poke. The wrapped MediaId (AnnouncementMediaId.Wrap) is
//     applied by this file exactly where the Orchestrator's own RenderAnnouncementAsync local function
//     applies it (PLAN T341) — replicating that one line of glue, not routing around it. Draining the
//     real queue through the real, container-resolved AnnouncementAiredDrainService.ProcessAsync (T343's
//     own directly-testable seam) is what actually stamps the row aired and writes the real booth log
//     row — the SAME two side effects a genuine engine-confirmed advance would produce.
//
//   * source='token' (Scenario 6): AnnouncementHistoryDto carries State/DeclineReason/CollapseCount/
//     AiredAt — never Source (that record's own remarks: "no Host-only fields added" beyond what the
//     page needs). There is no wire surface for this fact, so it is checked the only honest way
//     available: a direct read of the real station.announcement row this proof's own POST created
//     (TestStationDatabase.ReadAnnouncementSourceAsync) — never used for the AIRED stamp itself, which
//     stays exclusively the TrackAired path above (PLAN T345's own binding).
//
// The five lowercase wire state strings (SPEC F143.2) are asserted verbatim off the real
// GET /api/announcements endpoint throughout — never a Core-level enum name. 'expired' is the one state
// this file does not reach: SPEC F143.1's own 60s TTL floor makes a genuine expiry too slow for a wire
// proof to wait out honestly, and ExpireStaleAsync's own mechanics are already proven against a real
// Postgres fixture by GenWave.MediaLibrary.Tests/Specs/Story357_AnnouncementStore.cs — re-deriving that
// SQL here would not be a new wire fact.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Host;
using GenWave.Host.Announcements;
using GenWave.Host.Tests.Support;
using GenWave.Tts;
using Npgsql;

namespace GenWave.Host.Tests.Specs;

// ── Wire shapes (mirrors Story350_TruthLaneEndToEnd.cs's own narrower-than-the-DTO idiom: a
// `file`-scoped redefinition per spec file, never a cross-file reference to the server DTOs) ────────

file sealed record AnnouncementAcceptedWire(long Id);

file sealed record AnnouncementHistoryWire(
    long Id, string Message, bool Verbatim, string State, string? DeclineReason, int CollapseCount,
    DateTime CreatedAt, DateTime ExpiresAt, DateTime? AiredAt);

file sealed record LlmCallRow(string Kind, string Cause);

file sealed record LlmCallsSurfaceResponse(IReadOnlyList<LlmCallRow> Calls);

file sealed record AnnounceTokenGeneratedWire(string Token);

public static class FeaturePaWireProof
{
    public sealed class ScenarioVerbatimAnnouncementAirsWithinOneBreakCycle
        : IClassFixture<VerbatimAnnouncementArc>
    {
        readonly VerbatimAnnouncementArc arc;
        public ScenarioVerbatimAnnouncementAirsWithinOneBreakCycle(VerbatimAnnouncementArc arc) => this.arc = arc;

        [Fact]
        public void ThePostIsAccepted() => Assert.Equal(HttpStatusCode.OK, arc.PostStatus);

        [Fact]
        public void TheRealVendClaimsExactlyTheVerbatimRow() =>
            Assert.Equal((Count: 1, Verbatim: true), (Count: arc.ClaimedCount, Verbatim: arc.ClaimedVerbatim));

        [Fact]
        public void TheRenderedWireTextIsTheExactMessageWordForWord() =>
            // Case-insensitive, never fuzzy: gh-#541's speakability flatten (SpeechText.FlattenForSpeech,
            // NormalizingTtsSynthesizer's own real normalization pass — genuine production code on this
            // path, never bypassed) case-folds every render's wire text by design — the SAME documented
            // law Story358_AnnouncementFlavorEndToEnd.cs's own flavored-copy fact already asserts against
            // with OrdinalIgnoreCase. Word-for-word, case-folded-survival: not a byte-identical echo.
            Assert.Equal(VerbatimAnnouncementArc.Message, arc.CapturedSpeechInput, StringComparer.OrdinalIgnoreCase);

        [Fact]
        public void TheRowReachesAiredThroughTheRealSinkComposition() =>
            Assert.Equal("aired", arc.HistoryStateAfterAir);
    }

    public sealed class ScenarioFlavoredAnnouncementWithHealthyLlmAirsInCharacter
        : IClassFixture<FlavoredHealthyLlmArc>
    {
        readonly FlavoredHealthyLlmArc arc;
        public ScenarioFlavoredAnnouncementWithHealthyLlmAirsInCharacter(FlavoredHealthyLlmArc arc) => this.arc = arc;

        [Fact]
        public void TheFlavoredCopyIsNotNull() => Assert.NotNull(arc.FlavoredCopy);

        [Fact]
        public void TheAiredCopyContainsTheCaseFoldedCore() =>
            Assert.Contains(FlavoredHealthyLlmArc.Message, arc.CapturedSpeechInput, StringComparison.OrdinalIgnoreCase);

        [Fact]
        public void TheRowReachesAired() => Assert.Equal("aired", arc.HistoryState);

        [Fact]
        public void TheGateLoggedNoFabricationRejectForThisMessage() =>
            Assert.False(arc.LlmCallsShowFabricationRejectForAnnouncement);

        [Fact]
        public void TheLlmCallsSurfaceShowsTheAnnouncementLane() =>
            Assert.True(arc.LlmCallsShowSuccessForAnnouncement);
    }

    public sealed class ScenarioFlavoredAnnouncementWithLlmFencedAirsVerbatim
        : IClassFixture<LlmFencedArc>
    {
        readonly LlmFencedArc arc;
        public ScenarioFlavoredAnnouncementWithLlmFencedAirsVerbatim(LlmFencedArc arc) => this.arc = arc;

        [Fact]
        public void TheFlavorAttemptDegradesToNullTheFallbackLawsOwnSignal() => Assert.Null(arc.FlavoredCopy);

        [Fact]
        // Renamed at the T345 review: this fact pins the RENDER CHAIN preserving the copy exactly —
        // the ROUTING half (production's `flavoredText ?? announcement.Message`) is the Orchestrator's
        // and is pinned by T342's Orchestration net (the review's probe A reds there, not here).
        public void TheRenderChainCarriesTheOwnersExactWordsToTheSynthesizer() =>
            // Case-insensitive per gh-#541's speakability flatten — see VerbatimAnnouncementArc's own
            // sibling fact for the full rationale.
            Assert.Equal(LlmFencedArc.Message, arc.CapturedSpeechInput, StringComparer.OrdinalIgnoreCase);

        [Fact]
        public void TheRowStillReachesAiredDespiteTheFencedLlm() => Assert.Equal("aired", arc.HistoryState);

        [Fact]
        public void TheCauseSurfaceShowsExactlyOneHonestlyCountedFailedAttempt() =>
            Assert.Equal(1, arc.ConnectionFailureCountForAnnouncement);
    }

    public sealed class ScenarioHardDegradationForcesZeroLlmAttempts
        : IClassFixture<HardDegradationArc>
    {
        readonly HardDegradationArc arc;
        public ScenarioHardDegradationForcesZeroLlmAttempts(HardDegradationArc arc) => this.arc = arc;

        [Fact]
        public void TheFlavorAttemptDegradesToNull() => Assert.Null(arc.FlavoredCopy);

        [Fact]
        public void ZeroCallsEverReachedTheHealthyStubHardTakesTheFloorImmediately() =>
            Assert.Equal(0, arc.LlmStubRequestCount);

        [Fact]
        public void TheVerbatimFloorStillReachesAired() => Assert.Equal("aired", arc.HistoryState);

        [Fact]
        public void TheHardFloorAirsTheOwnersOwnWords() =>
            Assert.Equal(HardDegradationArc.Message, arc.CapturedSpeechInput, StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ScenarioThePrivacyArcDeclinesAndRefuses : IClassFixture<PrivacyArc>
    {
        readonly PrivacyArc arc;
        public ScenarioThePrivacyArcDeclinesAndRefuses(PrivacyArc arc) => this.arc = arc;

        [Fact]
        public void BeforeTheFlipTheUnclaimedRowReadsPending() =>
            Assert.Equal("pending", arc.PendingStateBeforeFlip);

        [Fact]
        public void BeforeTheFlipTheClaimedRowReadsClaimed() =>
            Assert.Equal("claimed", arc.ClaimedStateBeforeFlip);

        [Fact]
        public void TheAlreadyAiredRowIsUntouchedByTheFlip() =>
            Assert.Equal("aired", arc.AiredStateAfterFlip);

        [Fact]
        public void ThePendingRowDeclinesAtTheFlip() =>
            Assert.Equal("declined", arc.DeclinedPendingStateAfterFlip);

        [Fact]
        public void TheClaimedRowDeclinesAtTheFlipToo() =>
            Assert.Equal("declined", arc.DeclinedClaimedStateAfterFlip);

        [Fact]
        public void TheDeclineReasonIsExactlyStationWentPublic() =>
            Assert.Equal("station went public", arc.DeclineReasonForPending);

        [Fact]
        public void APostWhilePublicIsFourOhThree() =>
            Assert.Equal(HttpStatusCode.Forbidden, arc.PostWhilePublicStatus);

        [Fact]
        public void TheFourOhThreesDetailIsTheServersOwnF145Sentence() =>
            Assert.Equal(
                "The station is public (Station:SpectatorMode is on) — a public stream never carries the house's events (SPEC F145.1).",
                arc.PostWhilePublicDetail);
    }

    public sealed class ScenarioTheTokenDoorVerbatimFlowEndToEnd : IClassFixture<TokenDoorArc>
    {
        readonly TokenDoorArc arc;
        public ScenarioTheTokenDoorVerbatimFlowEndToEnd(TokenDoorArc arc) => this.arc = arc;

        [Fact]
        public void TheBearerAuthedPostIsAccepted() => Assert.Equal(HttpStatusCode.OK, arc.PostStatus);

        [Fact]
        public void TheRowsSourceColumnIsToken() => Assert.Equal("token", arc.SourceColumnValue);

        [Fact]
        public void TheBearerAuthedFlowStillReachesAired() => Assert.Equal("aired", arc.HistoryState);
    }
}

// ── Arc fixtures — each arranges its own ephemeral Postgres + production host exactly ONCE
// (IAsyncLifetime.InitializeAsync, shared across a Scenario's Facts via IClassFixture<T>) and tears
// both down before any Fact runs — only the captured VALUES below survive for the Facts to read. ─────

public sealed class VerbatimAnnouncementArc : IAsyncLifetime
{
    public const string Message = "Dinner is ready";

    public HttpStatusCode PostStatus { get; private set; }
    public int ClaimedCount { get; private set; }
    public bool ClaimedVerbatim { get; private set; }
    public string CapturedSpeechInput { get; private set; } = "";
    public string HistoryStateAfterAir { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var cacheRoot = PaWireProofSupport.FreshTempDir();
        await using var db = await TestStationDatabase.StartAsync();
        await using var kokoro = await KokoroSpeechStub.StartAsync();
        await using var factory = new PaWireProofWebFactory(db, kokoro.BaseUri, cacheRoot);
        var client = factory.CreateClient();
        await PaWireProofSupport.LoginAsync(client, PaWireProofWebFactory.Password);

        // POST — the accepting door (SPEC F143.1/.4/.5).
        var postResponse = await client.PostAsJsonAsync("/api/announcements", new { message = Message, verbatim = true });
        PostStatus = postResponse.StatusCode;
        var id = (await postResponse.Content.ReadFromJsonAsync<AnnouncementAcceptedWire>())!.Id;

        // Vend — the real IAnnouncementSource off the container.
        var source = factory.Services.GetRequiredService<IAnnouncementSource>();
        var claimed = await source.ClaimDeliverableAsync(2, CancellationToken.None);
        ClaimedCount = claimed.Count;
        var item = claimed.Single(i => i.Id == id);
        ClaimedVerbatim = item.Verbatim;

        // Render — the real IVerbatimSegmentRenderer; zero LLM anywhere on this path (F144.2).
        var renderer = factory.Services.GetRequiredService<IVerbatimSegmentRenderer>();
        var request = PaWireProofSupport.AnnouncementRequest(factory.Services);
        var rendered = await renderer.RenderAsync(request, new SegmentCopy(item.Message, FreshPerAiring: true), CancellationToken.None)
            ?? throw new InvalidOperationException("verbatim render unexpectedly returned null");
        CapturedSpeechInput = kokoro.Requests.Single().Input;

        // Air — the genuine TrackAired publish through the real CompositeStationEventSink.
        await PaWireProofSupport.PublishAiredAndDrainAsync(factory, id, rendered);

        // History — the real GET /api/announcements.
        var history = await PaWireProofSupport.GetHistoryAsync(client);
        HistoryStateAfterAir = history.Single(r => r.Id == id).State;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class FlavoredHealthyLlmArc : IAsyncLifetime
{
    public const string Message = "The garage sale starts at nine";
    const string CleanCopy = $"Quick note from the station: {Message}";

    public string? FlavoredCopy { get; private set; }
    public string CapturedSpeechInput { get; private set; } = "";
    public string HistoryState { get; private set; } = "";
    public bool LlmCallsShowSuccessForAnnouncement { get; private set; }
    public bool LlmCallsShowFabricationRejectForAnnouncement { get; private set; }

    public async Task InitializeAsync()
    {
        var cacheRoot = PaWireProofSupport.FreshTempDir();
        await using var db = await TestStationDatabase.StartAsync();
        await using var kokoro = await KokoroSpeechStub.StartAsync();
        await using var llm = await LlmCompletionsStub.StartAsync();
        llm.ReplyContent = CleanCopy;
        await using var factory = new PaWireProofWebFactory(db, kokoro.BaseUri, cacheRoot, llm.BaseUri.ToString());
        var client = factory.CreateClient();
        await PaWireProofSupport.LoginAsync(client, PaWireProofWebFactory.Password);

        var postResponse = await client.PostAsJsonAsync("/api/announcements", new { message = Message, verbatim = false });
        var id = (await postResponse.Content.ReadFromJsonAsync<AnnouncementAcceptedWire>())!.Id;

        var source = factory.Services.GetRequiredService<IAnnouncementSource>();
        var item = (await source.ClaimDeliverableAsync(2, CancellationToken.None)).Single(i => i.Id == id);

        // Flavor — the real IAnnouncementCopyWriter (LlmCopyWriter), against a real Kestrel completions
        // stub (Story358's own precedent, extended here into a real render).
        var request = PaWireProofSupport.AnnouncementRequest(factory.Services);
        var copyWriter = factory.Services.GetRequiredService<IAnnouncementCopyWriter>();
        FlavoredCopy = await copyWriter.WriteAnnouncementAsync(request, item.Message, CancellationToken.None);

        var renderer = factory.Services.GetRequiredService<IVerbatimSegmentRenderer>();
        var rendered = await renderer.RenderAsync(
                request, new SegmentCopy(FlavoredCopy ?? item.Message, FreshPerAiring: true), CancellationToken.None)
            ?? throw new InvalidOperationException("flavored render unexpectedly returned null");
        CapturedSpeechInput = kokoro.Requests.Single().Input;

        await PaWireProofSupport.PublishAiredAndDrainAsync(factory, id, rendered);

        var history = await PaWireProofSupport.GetHistoryAsync(client);
        HistoryState = history.Single(r => r.Id == id).State;

        var llmCalls = (await client.GetFromJsonAsync<LlmCallsSurfaceResponse>("/api/llm-calls"))!;
        LlmCallsShowSuccessForAnnouncement = llmCalls.Calls.Any(c => c.Kind == "announcement" && c.Cause == "success");
        LlmCallsShowFabricationRejectForAnnouncement = llmCalls.Calls.Any(c => c.Kind == "announcement" && c.Cause == "truthgatereject");
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class LlmFencedArc : IAsyncLifetime
{
    public const string Message = "The garage sale starts at nine";

    public string? FlavoredCopy { get; private set; }
    public string CapturedSpeechInput { get; private set; } = "";
    public string HistoryState { get; private set; } = "";
    public int ConnectionFailureCountForAnnouncement { get; private set; }

    public async Task InitializeAsync()
    {
        var cacheRoot = PaWireProofSupport.FreshTempDir();
        await using var db = await TestStationDatabase.StartAsync();
        await using var kokoro = await KokoroSpeechStub.StartAsync();
        // Connection refused, never a silent skip (Gh148_HealthContainersEndpoint.cs's own idiom): a
        // loopback port nothing listens on — no DNS wait, an immediate ECONNREFUSED.
        await using var factory = new PaWireProofWebFactory(db, kokoro.BaseUri, cacheRoot, llmEndpoint: "http://127.0.0.1:1");
        var client = factory.CreateClient();
        await PaWireProofSupport.LoginAsync(client, PaWireProofWebFactory.Password);

        var postResponse = await client.PostAsJsonAsync("/api/announcements", new { message = Message, verbatim = false });
        var id = (await postResponse.Content.ReadFromJsonAsync<AnnouncementAcceptedWire>())!.Id;

        var source = factory.Services.GetRequiredService<IAnnouncementSource>();
        var item = (await source.ClaimDeliverableAsync(2, CancellationToken.None)).Single(i => i.Id == id);

        var request = PaWireProofSupport.AnnouncementRequest(factory.Services);
        var copyWriter = factory.Services.GetRequiredService<IAnnouncementCopyWriter>();
        FlavoredCopy = await copyWriter.WriteAnnouncementAsync(request, item.Message, CancellationToken.None);

        var renderer = factory.Services.GetRequiredService<IVerbatimSegmentRenderer>();
        var rendered = await renderer.RenderAsync(
                request, new SegmentCopy(FlavoredCopy ?? item.Message, FreshPerAiring: true), CancellationToken.None)
            ?? throw new InvalidOperationException("verbatim-floor render unexpectedly returned null");
        CapturedSpeechInput = kokoro.Requests.Single().Input;

        await PaWireProofSupport.PublishAiredAndDrainAsync(factory, id, rendered);

        var history = await PaWireProofSupport.GetHistoryAsync(client);
        HistoryState = history.Single(r => r.Id == id).State;

        var llmCalls = (await client.GetFromJsonAsync<LlmCallsSurfaceResponse>("/api/llm-calls"))!;
        ConnectionFailureCountForAnnouncement =
            llmCalls.Calls.Count(c => c.Kind == "announcement" && c.Cause == "connectionfailure");
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class HardDegradationArc : IAsyncLifetime
{
    public const string Message = "The garage sale starts at nine";

    public string? FlavoredCopy { get; private set; }
    public int LlmStubRequestCount { get; private set; }
    public string HistoryState { get; private set; } = "";
    public string CapturedSpeechInput { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var cacheRoot = PaWireProofSupport.FreshTempDir();
        await using var db = await TestStationDatabase.StartAsync();
        await using var kokoro = await KokoroSpeechStub.StartAsync();
        // A perfectly healthy stub the station must never reach — Hard degradation takes the
        // verbatim floor immediately, with ZERO LLM calls (LlmCopyWriter.WriteAnnouncementAsync's own
        // MEDIUM-4 ruling, checked before anything else in that method).
        await using var llm = await LlmCompletionsStub.StartAsync();
        llm.ReplyContent = "Great tunes all night long, stick around!";
        await using var factory = new PaWireProofWebFactory(
            db, kokoro.BaseUri, cacheRoot, llm.BaseUri.ToString(), degradationPin: "hard");
        var client = factory.CreateClient();
        await PaWireProofSupport.LoginAsync(client, PaWireProofWebFactory.Password);

        // Apply the pin: LlmCopyWriter reads IDegradationModeReader.CurrentMode, a cached field only
        // DegradationController.Evaluate() updates — this proof calls the real, container-resolved
        // singleton's own Evaluate() once, standing in for the periodic health-probe hosted service
        // this factory removes along with every other IHostedService.
        factory.Services.GetRequiredService<DegradationController>().Evaluate();

        var postResponse = await client.PostAsJsonAsync("/api/announcements", new { message = Message, verbatim = false });
        var id = (await postResponse.Content.ReadFromJsonAsync<AnnouncementAcceptedWire>())!.Id;

        var source = factory.Services.GetRequiredService<IAnnouncementSource>();
        var item = (await source.ClaimDeliverableAsync(2, CancellationToken.None)).Single(i => i.Id == id);

        var request = PaWireProofSupport.AnnouncementRequest(factory.Services);
        var copyWriter = factory.Services.GetRequiredService<IAnnouncementCopyWriter>();
        FlavoredCopy = await copyWriter.WriteAnnouncementAsync(request, item.Message, CancellationToken.None);
        LlmStubRequestCount = llm.Requests.Count;

        var renderer = factory.Services.GetRequiredService<IVerbatimSegmentRenderer>();
        var rendered = await renderer.RenderAsync(
                request, new SegmentCopy(FlavoredCopy ?? item.Message, FreshPerAiring: true), CancellationToken.None)
            ?? throw new InvalidOperationException("verbatim-floor render unexpectedly returned null");

        // T345 review finding 2: capture what actually reached the synthesizer so the Hard floor's
        // WORDS are pinned, not just its state — mirrors the fenced arc's own capture.
        CapturedSpeechInput = kokoro.Requests.Single().Input;

        await PaWireProofSupport.PublishAiredAndDrainAsync(factory, id, rendered);

        var history = await PaWireProofSupport.GetHistoryAsync(client);
        HistoryState = history.Single(r => r.Id == id).State;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class PrivacyArc : IAsyncLifetime
{
    public string PendingStateBeforeFlip { get; private set; } = "";
    public string ClaimedStateBeforeFlip { get; private set; } = "";
    public string AiredStateAfterFlip { get; private set; } = "";
    public string DeclinedPendingStateAfterFlip { get; private set; } = "";
    public string DeclinedClaimedStateAfterFlip { get; private set; } = "";
    public string DeclineReasonForPending { get; private set; } = "";
    public HttpStatusCode PostWhilePublicStatus { get; private set; }
    public string PostWhilePublicDetail { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var cacheRoot = PaWireProofSupport.FreshTempDir();
        await using var db = await TestStationDatabase.StartAsync();
        await using var kokoro = await KokoroSpeechStub.StartAsync();
        await using var factory = new PaWireProofWebFactory(db, kokoro.BaseUri, cacheRoot);
        var client = factory.CreateClient();
        await PaWireProofSupport.LoginAsync(client, PaWireProofWebFactory.Password);

        var source = factory.Services.GetRequiredService<IAnnouncementSource>();
        var renderer = factory.Services.GetRequiredService<IVerbatimSegmentRenderer>();

        // Row A: the full private lifecycle to 'aired', before anything flips — proves an already-aired
        // row is untouched by a later flip (it is no longer live, so DeclineAllLiveAsync never reaches it).
        var idA = (await (await client.PostAsJsonAsync(
                "/api/announcements", new { message = "Row A airs before the flip", verbatim = true }))
            .Content.ReadFromJsonAsync<AnnouncementAcceptedWire>())!.Id;
        var itemA = (await source.ClaimDeliverableAsync(2, CancellationToken.None)).Single(i => i.Id == idA);
        var requestA = PaWireProofSupport.AnnouncementRequest(factory.Services);
        var renderedA = await renderer.RenderAsync(requestA, new SegmentCopy(itemA.Message, FreshPerAiring: true), CancellationToken.None)
            ?? throw new InvalidOperationException("row A render unexpectedly returned null");
        await PaWireProofSupport.PublishAiredAndDrainAsync(factory, idA, renderedA);

        // Row C: posted and claimed FIRST, while it is the only pending row left (row A's own claim
        // above already emptied the pending pool) — left claimed, never rendered/aired. Claiming it
        // before row B even exists is deliberate: ClaimDeliverableAsync claims OLDEST-first, so
        // claiming after both rows existed would sweep up row B too (it would be the older of the
        // two), defeating this scenario's own "one pending, one claimed" arrangement.
        var idC = (await (await client.PostAsJsonAsync(
                "/api/announcements", new { message = "Row C stays claimed until the flip", verbatim = true }))
            .Content.ReadFromJsonAsync<AnnouncementAcceptedWire>())!.Id;
        await source.ClaimDeliverableAsync(1, CancellationToken.None); // claims exactly C

        // Row B: posted AFTER row C's own claim — left pending, never claimed (no further claim call
        // reaches it).
        var idB = (await (await client.PostAsJsonAsync(
                "/api/announcements", new { message = "Row B stays pending until the flip", verbatim = true }))
            .Content.ReadFromJsonAsync<AnnouncementAcceptedWire>())!.Id;

        var interimHistory = await PaWireProofSupport.GetHistoryAsync(client);
        PendingStateBeforeFlip = interimHistory.Single(r => r.Id == idB).State;
        ClaimedStateBeforeFlip = interimHistory.Single(r => r.Id == idC).State;

        // The flip — the REAL settings PUT (SettingsController -> StationSettingsStore.WriteAsync ->
        // the real SettingChanged publish -> AnnouncementPrivacyFlipEventSink -> the real queue).
        var put = await client.PutAsJsonAsync(
            "/api/settings", new[] { new { key = "Station:SpectatorMode", value = "true" } });
        if (put.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"settings PUT unexpectedly returned {put.StatusCode}");

        var flipReader = factory.Services.GetRequiredService<ChannelReader<AnnouncementPrivacyFlipSignal>>();
        if (!flipReader.TryRead(out var flipSignal))
            throw new InvalidOperationException("the privacy-flip queue carried no signal after the settings PUT");
        await factory.Services.GetRequiredService<AnnouncementPrivacyFlipDrainService>()
            .ProcessAsync(flipSignal!, CancellationToken.None);

        var finalHistory = await PaWireProofSupport.GetHistoryAsync(client);
        AiredStateAfterFlip = finalHistory.Single(r => r.Id == idA).State;
        DeclinedPendingStateAfterFlip = finalHistory.Single(r => r.Id == idB).State;
        DeclinedClaimedStateAfterFlip = finalHistory.Single(r => r.Id == idC).State;
        DeclineReasonForPending = finalHistory.Single(r => r.Id == idB).DeclineReason ?? "";

        // The mid-flip POST — the door itself must now refuse (SPEC F145.1).
        var postWhilePublic = await client.PostAsJsonAsync(
            "/api/announcements", new { message = "Trying after the station went public", verbatim = true });
        PostWhilePublicStatus = postWhilePublic.StatusCode;
        PostWhilePublicDetail = await PaWireProofSupport.DetailAsync(postWhilePublic);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class TokenDoorArc : IAsyncLifetime
{
    public HttpStatusCode PostStatus { get; private set; }
    public string SourceColumnValue { get; private set; } = "";
    public string HistoryState { get; private set; } = "";

    public async Task InitializeAsync()
    {
        const string message = "From the smart speaker verbatim";
        var cacheRoot = PaWireProofSupport.FreshTempDir();
        // `await using` like every sibling arc (T345 review finding 1): a failure anywhere below
        // must still tear the ephemeral Postgres down — the bare `var` demonstrably leaked one
        // container per red run during the review's own mutation probes.
        await using var db = await TestStationDatabase.StartAsync();
        await using var kokoro = await KokoroSpeechStub.StartAsync();
        await using var factory = new PaWireProofWebFactory(db, kokoro.BaseUri, cacheRoot);
        var loggedInClient = factory.CreateClient();
        await PaWireProofSupport.LoginAsync(loggedInClient, PaWireProofWebFactory.Password);

        // The mint door is session-only (SPEC F145.3/.4) — generate via the logged-in session, then
        // authenticate every subsequent call with ONLY the revealed Bearer token, no cookie anywhere.
        var generate = await loggedInClient.PostAsync("/api/announcements/token", content: null);
        var plaintext = (await generate.Content.ReadFromJsonAsync<AnnounceTokenGeneratedWire>())!.Token;

        var bearerClient = factory.CreateClient();
        bearerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintext);

        var postResponse = await bearerClient.PostAsJsonAsync("/api/announcements", new { message, verbatim = true });
        PostStatus = postResponse.StatusCode;
        var id = (await postResponse.Content.ReadFromJsonAsync<AnnouncementAcceptedWire>())!.Id;

        var source = factory.Services.GetRequiredService<IAnnouncementSource>();
        var item = (await source.ClaimDeliverableAsync(2, CancellationToken.None)).Single(i => i.Id == id);
        var renderer = factory.Services.GetRequiredService<IVerbatimSegmentRenderer>();
        var request = PaWireProofSupport.AnnouncementRequest(factory.Services);
        var rendered = await renderer.RenderAsync(request, new SegmentCopy(item.Message, FreshPerAiring: true), CancellationToken.None)
            ?? throw new InvalidOperationException("token-door render unexpectedly returned null");

        await PaWireProofSupport.PublishAiredAndDrainAsync(factory, id, rendered);

        // The token also authorizes the history read (SPEC F145.3's family-scope grant, Story361's own
        // T344 ruling) — read it back over the SAME Bearer-only client.
        var history = await PaWireProofSupport.GetHistoryAsync(bearerClient);
        HistoryState = history.Single(r => r.Id == id).State;

        // source='token' — the one fact with no wire surface; see this file's own header remarks.
        SourceColumnValue = await db.ReadAnnouncementSourceAsync(id);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>Shared plumbing every Arc fixture above calls — kept here rather than duplicated per Arc,
/// mirroring Support/LlmCompletionsStub.cs's own "extract once a second/third caller needs it"
/// idiom, applied within this one file from the start since six Arcs need every member below.</summary>
file static class PaWireProofSupport
{
    public static string FreshTempDir() => Path.Combine(Path.GetTempPath(), "genwave-pawire-" + Guid.NewGuid().ToString("N"));

    // Login + the minimal announcement SegmentRequest shape both moved to
    // Support/AnnouncementWireSupport.cs (T352 review — Story364_TheGateRulesOnTheWire.cs
    // became a second caller) — thin delegations kept here so every existing call site below reads
    // unchanged. AnnouncementRequest() now takes the factory's own IServiceProvider (T352 review
    // round 2, HIGH-1): it reads Station:Name/Voice/Id live off that container instead of a baked
    // literal, so a factory that ever overrides those keys differently is reflected here too.
    public static Task LoginAsync(HttpClient client, string password) =>
        AnnouncementWireSupport.LoginAsync(client, password);

    public static SegmentRequest AnnouncementRequest(IServiceProvider services) =>
        AnnouncementWireSupport.AnnouncementRequest(services);

    /// <summary>Applies the Orchestrator's own MediaId-wrap (AnnouncementMediaId.Wrap, PLAN T341) —
    /// replicating that one line of glue, not routing around it — then publishes the genuine TrackAired
    /// through the real, container-resolved IStationEventSink and drains the real queue through the
    /// real AnnouncementAiredDrainService.ProcessAsync (T343's own directly-testable seam). This is the
    /// aired stamp for every Arc above — never a direct IAnnouncementLifecycle.MarkAiredAsync poke.</summary>
    public static async Task PublishAiredAndDrainAsync(WebApplicationFactory<Program> factory, long announcementId, MediaItem rendered)
    {
        var wrappedMediaId = AnnouncementMediaId.Wrap(announcementId, rendered.MediaId);
        var sink = factory.Services.GetRequiredService<IStationEventSink>();
        sink.Publish(new TrackAired(
            wrappedMediaId, rendered.Title, rendered.Artist, GainDb: 0.0, DateTimeOffset.UtcNow, rendered.DurationMs,
            SegmentKind: SegmentKind.Announcement));

        var reader = factory.Services.GetRequiredService<ChannelReader<AnnouncementAiredSignal>>();
        if (!reader.TryRead(out var signal))
            throw new InvalidOperationException("the aired-confirmation queue carried no signal after the TrackAired publish");

        await factory.Services.GetRequiredService<AnnouncementAiredDrainService>().ProcessAsync(signal!, CancellationToken.None);
    }

    public static async Task<IReadOnlyList<AnnouncementHistoryWire>> GetHistoryAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<AnnouncementHistoryWire>>("/api/announcements"))!;

    public static async Task<string> DetailAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("detail").GetString() ?? "";
    }
}

/// <summary>
/// This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/> harness
/// (Support/EphemeralStationDatabase.cs — T351 review hoist; that type's own remarks carry the full
/// "which compose file, why a unique project name + OS-assigned port" rationale). Supplies only what
/// genuinely varies for THIS file: the <c>"genwave-pawire"</c> compose project-name prefix, and the
/// one extra query (<see cref="ReadAnnouncementSourceAsync"/>) no other caller needs.
/// </summary>
file sealed class TestStationDatabase : EphemeralStationDatabase
{
    TestStationDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<TestStationDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-pawire");
        var db = new TestStationDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }

    /// <summary>Direct SQL escape hatch for the one fact this proof cannot reach any other way — see
    /// this file's own header remarks ("source='token'"). Never used for the aired stamp itself.</summary>
    public async Task<string> ReadAnnouncementSourceAsync(long id)
    {
        await using var conn = new NpgsqlConnection(StationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select source from station.announcement where id = @id";
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteScalarAsync() as string
            ?? throw new InvalidOperationException($"no station.announcement row for id {id}");
    }
}

/// <summary>One <c>POST /v1/audio/speech</c> request this stub captured — the wire text KokoroTtsSynthesizer
/// actually sent, read back the same "real HTTP round trip, not in-process state" way
/// Support/LlmCompletionsStub.cs's own CapturedCompletionsRequest already does one seam over.</summary>
file sealed record CapturedSpeechRequest(string Input, string Voice);

/// <summary>
/// Minimal Kestrel-backed stub for kokoro-fastapi's OpenAI-compatible <c>POST /v1/audio/speech</c> —
/// the ONE fake in this file's entire render chain (see this file's own header remarks for why: Kokoro
/// itself has no reachable port under the bench freeze, and everything ABOVE this one network hop —
/// NormalizingTtsSynthesizer's real correction/pronunciation pass, FallbackTtsSynthesizer,
/// KokoroTtsSynthesizer's own request-building — stays genuine production code). Answers every request
/// with a real, non-zero-duration mono WAV (the FakeCrosstalkVoiceSynthesizer precedent, GenWave.Tts.Tests
/// — redefined here since this test project has no reference to that one, the "redefine, don't reach
/// across test PROJECTS" convention Story186_CorrectionsObservability.cs's own header note explains) so
/// ffmpeg's real loudness/cue analysis downstream has genuine audio to probe, never a zero-sample stand-in.
/// </summary>
file sealed class KokoroSpeechStub : IAsyncDisposable
{
    readonly WebApplication app;
    readonly object gate = new();
    readonly List<CapturedSpeechRequest> requests = [];

    public Uri BaseUri { get; }

    public IReadOnlyList<CapturedSpeechRequest> Requests { get { lock (gate) return requests.ToArray(); } }

    KokoroSpeechStub(WebApplication app, Uri baseUri)
    {
        this.app = app;
        BaseUri = baseUri;
    }

    public static async Task<KokoroSpeechStub> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        KokoroSpeechStub? stubRef = null;

        app.MapPost("/v1/audio/speech", async (HttpContext ctx) =>
        {
            var stub = stubRef ?? throw new InvalidOperationException("KokoroSpeechStub not initialized");
            var payload = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body, cancellationToken: ctx.RequestAborted);
            var input = payload.TryGetProperty("input", out var inputProp) ? inputProp.GetString() ?? "" : "";
            var voice = payload.TryGetProperty("voice", out var voiceProp) ? voiceProp.GetString() ?? "" : "";

            lock (stub.gate)
                stub.requests.Add(new CapturedSpeechRequest(input, voice));

            ctx.Response.ContentType = "audio/wav";
            var wav = CreateToneWav(seconds: 0.3, amplitudeFraction: 0.2);
            await ctx.Response.Body.WriteAsync(wav, ctx.RequestAborted);
        });

        await app.StartAsync();
        var stub = new KokoroSpeechStub(app, new Uri(app.Urls.First()));
        stubRef = stub;
        return stub;
    }

    /// <summary>A real mono 16-bit PCM WAV — mirrors GenWave.Tts.Tests.Fakes.FakeCrosstalkVoiceSynthesizer's
    /// own identically-purposed helper (redefined here per this class's own remarks): real, non-silent
    /// samples so ffmpeg's duration probing/loudness analysis behaves exactly as it would on genuine
    /// speech audio, without needing a real TTS engine anywhere in this suite.</summary>
    static byte[] CreateToneWav(double seconds, double amplitudeFraction, double frequencyHz = 440.0)
    {
        const int sampleRate = 44100;
        const short bitsPerSample = 16;
        const short channels = 1;
        var amplitude = amplitudeFraction * short.MaxValue;

        var sampleCount = Math.Max(1, (int)Math.Round(seconds * sampleRate));
        var dataSize = sampleCount * channels * (bitsPerSample / 8);
        var bytes = new byte[44 + dataSize];

        bytes[0] = (byte)'R'; bytes[1] = (byte)'I'; bytes[2] = (byte)'F'; bytes[3] = (byte)'F';
        WriteInt32LE(bytes, 4, 36 + dataSize);
        bytes[8] = (byte)'W'; bytes[9] = (byte)'A'; bytes[10] = (byte)'V'; bytes[11] = (byte)'E';
        bytes[12] = (byte)'f'; bytes[13] = (byte)'m'; bytes[14] = (byte)'t'; bytes[15] = (byte)' ';
        WriteInt32LE(bytes, 16, 16);
        WriteInt16LE(bytes, 20, 1);
        WriteInt16LE(bytes, 22, channels);
        WriteInt32LE(bytes, 24, sampleRate);
        WriteInt32LE(bytes, 28, sampleRate * channels * (bitsPerSample / 8));
        WriteInt16LE(bytes, 32, (short)(channels * (bitsPerSample / 8)));
        WriteInt16LE(bytes, 34, bitsPerSample);
        bytes[36] = (byte)'d'; bytes[37] = (byte)'a'; bytes[38] = (byte)'t'; bytes[39] = (byte)'a';
        WriteInt32LE(bytes, 40, dataSize);

        for (var i = 0; i < sampleCount; i++)
        {
            var sample = (short)(amplitude * Math.Sin(2 * Math.PI * frequencyHz * i / sampleRate));
            WriteInt16LE(bytes, 44 + (i * 2), sample);
        }

        return bytes;
    }

    static void WriteInt32LE(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    static void WriteInt16LE(byte[] buf, int offset, short value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();
}

/// <summary>
/// Boots the real production composition root (Program.cs) against a real ephemeral Postgres
/// (<see cref="TestStationDatabase"/>) and a real Kokoro-shaped HTTP endpoint
/// (<see cref="KokoroSpeechStub"/>) — mirrors LlmCompletionsWebFactory's (Support/LlmCompletionsStub.cs)
/// own "only IHostedService removed, everything else genuine" posture, widened here to the whole
/// station config surface production's own compose.yaml sets (Station:Id/Name/Voice/Scope — the exact
/// four keys compose.yaml itself overrides; everything else rides appsettings.json's own shipped
/// defaults, unchanged). <see cref="llmEndpoint"/> defaults to empty — the disabled state (LlmOptions'
/// own remarks) — since most Arcs above never touch the flavored path at all.
/// </summary>
file sealed class PaWireProofWebFactory(
    TestStationDatabase db,
    Uri kokoroEndpoint,
    string ttsCacheRoot,
    string llmEndpoint = "",
    string degradationPin = "auto") : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story345-pa-wire-proof";
    internal const string Model = "test-model-story345";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);

        // The exact four Station:* keys compose.yaml itself overrides in production (grep compose.yaml
        // for Station__Id/Station__Name/Station__Voice/Station__Scope__LibraryIds__0) — every other
        // Station:* leaf rides appsettings.json's own shipped default, unchanged.
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.UseSetting("Tts:Endpoint", kokoroEndpoint.ToString());
        builder.UseSetting("Tts:CacheRoot", ttsCacheRoot);

        builder.UseSetting("Llm:Endpoint", llmEndpoint);
        builder.UseSetting("Llm:Model", Model);
        builder.UseSetting("Llm:DegradationPin", degradationPin);

        builder.ConfigureTestServices(services =>
        {
            // No Liquidsoap/Kokoro-model/real-background-loop reach during this test — mirrors every
            // other WebApplicationFactory-based spec in this suite.
            services.RemoveAll<IHostedService>();

            // Re-registered as themselves (AddHostedService<T> never exposes T for direct resolution) —
            // the Story343_AnnouncementLifecycleSmoke.cs precedent this file's own header cites.
            services.AddSingleton<AnnouncementAiredDrainService>();
            services.AddSingleton<AnnouncementPrivacyFlipDrainService>();
        });
    }
}
