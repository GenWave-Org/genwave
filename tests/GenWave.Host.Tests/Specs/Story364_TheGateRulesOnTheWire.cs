// STORY-364 + STORY-365 — the gate rules proven on the production path (PLAN T352, the T335 recipe).
//
// BDD specification — xUnit. This file's ORIGINAL header assumed an announcement-shaped request
// through POST /api/personas/preview — that path does not exist: PersonaPreviewRequest carries no
// message field, and no HTTP surface renders an announcement's flavored copy synchronously (the
// Orchestrator's own vend step does that, inside the background playout loop this file's own factory
// removes along with every other IHostedService). The HONEST deployed path for the announcement lane
// is Story345_PaWireProof.cs's own FlavoredHealthyLlmArc arc, reused here: POST /api/announcements
// (the real door, a logged-in client) → claim via the real, container-resolved IAnnouncementSource →
// the real IAnnouncementCopyWriter.WriteAnnouncementAsync (LlmCopyWriter itself, through the
// production DI graph) against a scripted completions stub (Support/LlmCompletionsStub.cs) —
// FlavoredCopy non-null means the flavored lane cleared the gate; null means the F144.4 verbatim
// floor. This file stops at the copy writer — no render, no air; T345/T358 already prove that half.
// Every SegmentRequest below is built by Support/AnnouncementWireSupport.cs's own AnnouncementRequest()
// off the arc's OWN factory container — station name/voice/id read live off
// IOptionsMonitor<StationOptions>, never a literal — so a factory that configures Station:Name
// differently (see TheDriftedStationNameArc below) genuinely changes what the gate checks (T352
// review round 2, HIGH-1).
//
// The one genuine STORY-364 AC5 exhibit that DOES have an HTTP surface — a LeadIn preview naming the
// station — still goes through the real POST /api/personas/preview endpoint below, unchanged from
// before F138.8. It needs no Postgres (no announcement row involved), so it runs against a separate,
// DB-less factory (LeadInPreviewWebFactory) instead of this file's other arcs' ephemeral Postgres —
// see TheLeadInPreviewArc's own remarks (MEDIUM-2/LOW-4, T352 review round 2) for why its own facts
// pin the prompt round-trip, not F138.8.
//
// Station named "GWAV 108.8" everywhere in this file EXCEPT TheDriftedStationNameArc, which sets a
// different Station:Name on purpose (HIGH-1's own pin) — the T335 recipe's own "real Kestrel, real DI
// graph, scripted completions stub" shape, extended here with a real ephemeral Postgres
// (Support/EphemeralStationDatabase.cs) since the announcement door writes a real row this file must
// claim back through the real IAnnouncementSource — the same reason Story345_PaWireProof.cs needed
// one. Each scenario is its own self-contained arc: its OWN ephemeral Postgres, its OWN
// WebApplicationFactory, arranged exactly ONCE inside an IAsyncLifetime fixture shared across that
// scenario's Facts via IClassFixture<T> — Story345_PaWireProof.cs's own arc shape, applied here. The
// four announcement-lane arcs share one arrange (GateRuleAnnouncementArc below, T352 review round 2,
// MEDIUM-3) since only the stub's script, the expected ring cause, and (once) the station name
// actually differ between them.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Host;
using GenWave.Host.Options;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

// ── Wire shapes (mirrors Story345_PaWireProof.cs's/Story350_TruthLaneEndToEnd.cs's own
// narrower-than-the-DTO idiom: a `file`-scoped redefinition per spec file). ─────────────────────────

file sealed record AnnouncementAcceptedWire(long Id);

file sealed record LlmCallRow(string Kind, string Cause);

file sealed record LlmCallsSurfaceResponse(IReadOnlyList<LlmCallRow> Calls);

file sealed record PersonaPreviewWire(string Text);

public static class FeatureTheGateRulesOnTheWire
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the gate clears the exhibits F138.8/F144.3 exist for
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheReaskCopyPassesTheGateOnTheWire : IClassFixture<TheReaskCopyArc>
    {
        readonly TheReaskCopyArc arc;
        public ScenarioTheReaskCopyPassesTheGateOnTheWire(TheReaskCopyArc arc) => this.arc = arc;

        // LOW-5: the door itself, named as its own fact — a 403/429 here should read as a red on
        // THIS line, never an opaque Single()-on-empty-list throw three lines into InitializeAsync.
        [Fact]
        public void ThePostIsAccepted() => Assert.Equal(HttpStatusCode.OK, arc.PostStatus);

        // STORY-364 AC1 + STORY-365 AC1 on the wire: the station's own "108.8" is a supported fact
        // (F138.8) and the terminal-punctuation/case difference ("hot." vs "HOT!") never trips
        // containment (F144.3 amended) — together, this exhibit clears the gate on the FIRST ask.
        [Fact]
        public void TheFlavoredCopyIsNotNull() => Assert.NotNull(arc.FlavoredCopy);

        [Fact]
        public void TheFlavoredCopyNamesTheStation() =>
            Assert.Contains("GWAV 108.8", arc.FlavoredCopy ?? "", StringComparison.Ordinal);

        [Fact]
        public void TheRingRecordsSuccessForTheAnnouncementLane() =>
            Assert.True(arc.LlmCallsShowExpectedCause);

        // LOW-6: pins the comment above's own claim ("clears the gate on the FIRST ask") as a fact,
        // not just prose — exactly one completions call, no re-ask the pass path never needed.
        [Fact]
        public void TheStubSawExactlyOneRequest() => Assert.Equal(1, arc.LlmRequestCount);
    }

    public sealed class ScenarioTheDriftedStationNameStillRejects : IClassFixture<TheDriftedStationNameArc>
    {
        readonly TheDriftedStationNameArc arc;
        public ScenarioTheDriftedStationNameStillRejects(TheDriftedStationNameArc arc) => this.arc = arc;

        [Fact]
        public void ThePostIsAccepted() => Assert.Equal(HttpStatusCode.OK, arc.PostStatus);

        // HIGH-1 (T352 review round 2) — the config→request→gate hop, pinned: this arc posts the
        // EXACT same reply ScenarioTheReaskCopyPassesTheGateOnTheWire clears above (it names
        // "GWAV 108.8"); the only thing that differs is this arc's own factory Station:Name
        // ("DRIFTED FM 99.9"). A station literal baked into AnnouncementRequest() (the pre-review
        // bug) could never fail this fact — it would still say "GWAV 108.8" no matter what the
        // factory actually configured, since it never read the factory at all.
        [Fact]
        public void TheFlavoredCopyIsNull() => Assert.Null(arc.FlavoredCopy);

        [Fact]
        public void TheRingRecordsTruthGateRejectForTheAnnouncementLane() =>
            Assert.True(arc.LlmCallsShowExpectedCause);

        [Fact]
        public void TheAskAndTheReaskBothFired() => Assert.Equal(2, arc.LlmRequestCount);
    }

    public sealed class ScenarioALeadInPreviewStillNamesTheStation : IClassFixture<TheLeadInPreviewArc>
    {
        readonly TheLeadInPreviewArc arc;
        public ScenarioALeadInPreviewStillNamesTheStation(TheLeadInPreviewArc arc) => this.arc = arc;

        // STORY-364 AC5 on the wire — a regression pin: the pre-F138.8 LeadIn lane (no fact block, no
        // announcement core) keeps passing the gate exactly as it did before. MEDIUM-2 (T352 review
        // round 2): a LeadIn preview's SegmentRequest carries Track: null, never a ContextSegment, so
        // factBlock is null and CopyClaims.CheckFacts never runs at all for this kind — the 200 below,
        // plus the body naming the station, is the completions stub's own canned reply round-tripping
        // unchanged. It pins the preview endpoint's HTTP round-trip, not the F138.8 fact gate.
        [Fact]
        public void ThePreviewAnswersTwoHundred() => Assert.Equal(HttpStatusCode.OK, arc.PreviewStatus);

        [Fact]
        public void ThePreviewBodyNamesTheStation() =>
            Assert.Contains("GWAV 108.8", arc.PreviewText, StringComparison.Ordinal);

        // The load-bearing half of this arc (MEDIUM-2): the PROMPT the stub actually received, built
        // by the real LlmPromptBuilder off PersonaController.Preview's own IOptionsMonitor
        // <StationOptions> read, carries the station's live-configured name — read here off the same
        // options seam, never a second "GWAV 108.8" literal duplicated into the assertion.
        [Fact]
        public void ThePreviewPromptCarriesTheConfiguredStationName() =>
            Assert.Contains(arc.ConfiguredStationName, arc.CapturedPrompt, StringComparison.Ordinal);

        // A single preview call fires a single completions request — pinned as its own fact so a
        // regression that fires twice reds here by name, not as an opaque Single()-on-many throw.
        [Fact]
        public void TheStubSawExactlyOneRequest() => Assert.Equal(1, arc.LlmRequestCount);
    }

    // ---------------------------------------------------------------------
    // SAD PATH — fabrication and paraphrase still die at the gate
    // ---------------------------------------------------------------------

    public sealed class ScenarioAttemptOneIsStillRejected : IClassFixture<TheAttemptOneRejectedArc>
    {
        readonly TheAttemptOneRejectedArc arc;
        public ScenarioAttemptOneIsStillRejected(TheAttemptOneRejectedArc arc) => this.arc = arc;

        [Fact]
        public void ThePostIsAccepted() => Assert.Equal(HttpStatusCode.OK, arc.PostStatus);

        // STORY-365 AC6 on the wire, the 2026-08-28 attempt shape: the fabricated reply never
        // mentions the owner's core at all, on either the first ask or the re-ask — the ladder
        // exhausts and the verbatim floor's own null signal fires.
        [Fact]
        public void TheFlavoredCopyIsNull() => Assert.Null(arc.FlavoredCopy);

        [Fact]
        public void TheRingRecordsTruthGateRejectForTheAnnouncementLane() =>
            Assert.True(arc.LlmCallsShowExpectedCause);

        // LOW-6: "ask + re-ask both refused" is a claim about COUNT, not only cause — pin it.
        [Fact]
        public void TheAskAndTheReaskBothFired() => Assert.Equal(2, arc.LlmRequestCount);
    }

    public sealed class ScenarioAParaphraseIsStillRejected : IClassFixture<TheParaphraseRejectedArc>
    {
        readonly TheParaphraseRejectedArc arc;
        public ScenarioAParaphraseIsStillRejected(TheParaphraseRejectedArc arc) => this.arc = arc;

        [Fact]
        public void ThePostIsAccepted() => Assert.Equal(HttpStatusCode.OK, arc.PostStatus);

        // STORY-365 AC6's second exhibit on the wire: a paraphrase carries the same gist as the
        // owner's message but not its own word sequence — the amended F144.3 word-run check still
        // refuses it, proving the amendment did not turn containment into a vacuous gist check.
        [Fact]
        public void TheFlavoredCopyIsNull() => Assert.Null(arc.FlavoredCopy);

        [Fact]
        public void TheRingRecordsTruthGateRejectForTheAnnouncementLane() =>
            Assert.True(arc.LlmCallsShowExpectedCause);

        [Fact]
        public void TheAskAndTheReaskBothFired() => Assert.Equal(2, arc.LlmRequestCount);
    }
}

// ── Arc fixtures — each arranges its own ephemeral Postgres + production host exactly ONCE
// (IAsyncLifetime.InitializeAsync, shared across a Scenario's Facts via IClassFixture<T>), mirroring
// Story345_PaWireProof.cs's own arc shape. ────────────────────────────────────────────────────────

/// <summary>
/// Shared arrange for every announcement-lane gate arc below (MEDIUM-3, T352 review round 2): the pass
/// exhibit, the two reject exhibits, and the drifted-station-name exhibit differ in exactly THREE
/// things — what the completions stub answers, what <c>/api/llm-calls</c> Cause the flavored write is
/// expected to land under, and (only for <see cref="TheDriftedStationNameArc"/>) which Station:Name
/// the factory boots with. Everything else — provisioning the ephemeral Postgres, posting the real
/// door, claiming the real vend, calling the real <see cref="IAnnouncementCopyWriter"/>, reading the
/// real ring back — is arranged exactly once here rather than three (now four) line-identical copies,
/// mirroring Story345_PaWireProof.cs's own "one arrange, several Facts" shape one level higher (a base
/// class shared by a FAMILY of near-identical arcs, not per-arc).
/// </summary>
public abstract class GateRuleAnnouncementArc : IAsyncLifetime
{
    // The AC1 core (STORY-364, STORY-365) — the owner's own message, posted verbatim=false so the
    // flavored lane is the one under test. Identical across every arc below; only the stub's script
    // (ConfigureStub) and, for the drifted arc, the station name actually vary.
    protected const string Message = "Dinner is ready — come and get it while it's hot.";

    /// <summary>Station:Name this arc's own factory boots with — "GWAV 108.8" for every arc except
    /// <see cref="TheDriftedStationNameArc"/> (HIGH-1's own pin: the SAME re-ask copy that clears the
    /// gate when the station really is GWAV 108.8 must NOT clear it once Station:Name says something
    /// else — see that arc's own remarks).</summary>
    protected virtual string StationName => "GWAV 108.8";

    /// <summary>What the completions stub answers, call by call — the one thing that actually
    /// distinguishes the pass arc from each reject arc's own fabrication/paraphrase. Plain data, not a
    /// stub-configuring delegate (<see cref="LlmCompletionsStub"/> is `internal`, one accessibility
    /// notch below this `public` base class — see Support/LlmCompletionsStub.cs's own remarks — so a
    /// hook exposing it directly cannot itself be `protected` here); queued onto the stub verbatim
    /// (<see cref="LlmCompletionsStub.QueueReplies"/>), so a single-entry list behaves exactly like the
    /// old single <c>ReplyContent</c> assignment for an arc whose gate clears on the first ask.</summary>
    protected abstract IReadOnlyList<string> StubReplies { get; }

    /// <summary>The <c>/api/llm-calls</c> Cause this arc's flavored write is expected to land under —
    /// "success" for the pass arc, "truthgatereject" for every reject arc.</summary>
    protected abstract string ExpectedRingCause { get; }

    public HttpStatusCode PostStatus { get; private set; }
    public string? FlavoredCopy { get; private set; }
    public bool LlmCallsShowExpectedCause { get; private set; }
    public int LlmRequestCount { get; private set; }

    public async Task InitializeAsync()
    {
        await using var db = await GateRulesTestDatabase.StartAsync();
        await using var llm = await LlmCompletionsStub.StartAsync();
        llm.QueueReplies([.. StubReplies]);
        await using var factory = new GateRulesWebFactory(db, llm.BaseUri.ToString(), StationName);
        var client = factory.CreateClient();
        await AnnouncementWireSupport.LoginAsync(client, GateRulesWebFactory.Password);

        var postResponse = await client.PostAsJsonAsync("/api/announcements", new { message = Message, verbatim = false });
        PostStatus = postResponse.StatusCode;
        var id = (await postResponse.Content.ReadFromJsonAsync<AnnouncementAcceptedWire>())!.Id;

        var source = factory.Services.GetRequiredService<IAnnouncementSource>();
        var item = (await source.ClaimDeliverableAsync(2, CancellationToken.None)).Single(i => i.Id == id);

        var copyWriter = factory.Services.GetRequiredService<IAnnouncementCopyWriter>();
        FlavoredCopy = await copyWriter.WriteAnnouncementAsync(
            AnnouncementWireSupport.AnnouncementRequest(factory.Services), item.Message, CancellationToken.None);

        LlmRequestCount = llm.Requests.Count;

        var llmCalls = (await client.GetFromJsonAsync<LlmCallsSurfaceResponse>("/api/llm-calls"))!;
        LlmCallsShowExpectedCause =
            llmCalls.Calls.Any(c => c.Kind == "announcement" && c.Cause == ExpectedRingCause);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class TheReaskCopyArc : GateRuleAnnouncementArc
{
    // The 2026-08-28 exhibit (STORY-364 AC1): case differs ("HOT!" for "hot.") and terminal
    // punctuation differs, and the copy names the station's own call sign "108.8".
    public const string ReaskCopy =
        "Well, rockers! This one's hot off the grill for ya! Dinner is ready — come and get it " +
        "while it's HOT! GWAV 108.8... Keep those fists pumping!";

    protected override IReadOnlyList<string> StubReplies => [ReaskCopy];
    protected override string ExpectedRingCause => "success";
}

public sealed class TheDriftedStationNameArc : GateRuleAnnouncementArc
{
    // HIGH-1 (T352 review round 2) — reuses TheReaskCopyArc's OWN exhibit verbatim (never a second,
    // near-identical copy): the only variable this arc changes is StationName below.
    protected override string StationName => "DRIFTED FM 99.9";
    // Two entries, not one (unlike TheReaskCopyArc's own single-entry StubReplies): this arc's own
    // ask is expected to fail, so the re-ask fires too and must see the SAME drifted-name-breaking
    // copy, never the stub's unrelated default fallback.
    protected override IReadOnlyList<string> StubReplies => [TheReaskCopyArc.ReaskCopy, TheReaskCopyArc.ReaskCopy];
    protected override string ExpectedRingCause => "truthgatereject";
}

public sealed class TheAttemptOneRejectedArc : GateRuleAnnouncementArc
{
    // The 2026-08-28 attempt shape (STORY-365 AC6): fabricates a persona ("the Metal Maven") and an
    // event the owner never wrote — the owner's own core is entirely absent.
    public const string AttemptOneFabrication =
        "Alright rockers, listen up! It's me, the Metal Maven, swinging into action here on GWAV 108.8. " +
        "Rumor has it our station owner whipped up a feast for us hungry listeners!";

    // Both the first ask and the re-ask fabricate — the ladder's one re-ask does not save it.
    protected override IReadOnlyList<string> StubReplies => [AttemptOneFabrication, AttemptOneFabrication];
    protected override string ExpectedRingCause => "truthgatereject";
}

public sealed class TheParaphraseRejectedArc : GateRuleAnnouncementArc
{
    // STORY-365 AC6's second exhibit — same gist as Message, none of its own word sequence.
    public const string ParaphraseCopy = "Dinner's ready and steamin' hot, so dig in while it lasts!";

    protected override IReadOnlyList<string> StubReplies => [ParaphraseCopy, ParaphraseCopy];
    protected override string ExpectedRingCause => "truthgatereject";
}

public sealed class TheLeadInPreviewArc : IAsyncLifetime
{
    public HttpStatusCode PreviewStatus { get; private set; }
    public string PreviewText { get; private set; } = "";

    /// <summary>Station:Name read live off <see cref="IOptionsMonitor{TOptions}"/> — the SAME options
    /// seam <c>PersonaController.Preview</c> itself reads (MEDIUM-2, T352 review round 2) — captured
    /// here so <see cref="CapturedPrompt"/> can be checked against it rather than a second, drifting
    /// "GWAV 108.8" literal.</summary>
    public string ConfiguredStationName { get; private set; } = "";

    /// <summary>The system+user prompt <see cref="LlmCompletionsStub"/> actually received for this
    /// preview's one completions call (T335's own capture) — what makes this arc's own facts pin the
    /// real production wire, not the stub's canned reply echoing itself back (see this arc's own
    /// Scenario remarks).</summary>
    public string CapturedPrompt { get; private set; } = "";

    /// <summary>How many completions calls this preview actually made — captured as its own fact
    /// (mirrors <see cref="GateRuleAnnouncementArc.LlmRequestCount"/> one type up) so a wiring bug that
    /// fires the stub twice reads as a named, counted red here rather than an opaque
    /// <c>Single()</c>-on-many-elements throw three lines into <see cref="InitializeAsync"/>.</summary>
    public int LlmRequestCount { get; private set; }

    public async Task InitializeAsync()
    {
        // LOW-4/MEDIUM-2 (T352 review round 2): this arc never claims an announcement row — it is a
        // straight PersonaController.Preview call — so it needs no Postgres at all. Uses the SAME
        // no-DB shape Support/LlmCompletionsStub.cs's own LlmCompletionsWebFactory already establishes
        // (Story196/Story353's own precedent), not EphemeralStationDatabase; LeadInPreviewWebFactory
        // below is that exact shape plus the Station:* overrides this file's other arcs also carry.
        await using var llm = await LlmCompletionsStub.StartAsync();
        llm.ReplyContent = "Great tune coming up — you're right now on GWAV 108.8.";
        await using var factory = new LeadInPreviewWebFactory(llm.BaseUri.ToString());
        var client = factory.CreateClient();
        await AnnouncementWireSupport.LoginAsync(client, LeadInPreviewWebFactory.Password);

        var response = await client.PostAsJsonAsync("/api/personas/preview", new { kind = "LeadIn" });
        PreviewStatus = response.StatusCode;
        if (response.StatusCode == HttpStatusCode.OK)
            PreviewText = (await response.Content.ReadFromJsonAsync<PersonaPreviewWire>())!.Text;

        ConfiguredStationName = factory.Services.GetRequiredService<IOptionsMonitor<StationOptions>>().CurrentValue.Name;
        LlmRequestCount = llm.Requests.Count;
        var captured = llm.Requests[0];
        CapturedPrompt = $"{captured.SystemPrompt}\n{captured.UserPrompt}";
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/> harness
/// (Support/EphemeralStationDatabase.cs — see that type's own remarks for the full "which compose
/// file, why a unique project name + OS-assigned port" rationale) — supplies only the
/// <c>"genwave-gaterules"</c> compose project-name prefix this file needs; no extra query methods
/// (unlike Story345_PaWireProof.cs's own TestStationDatabase), since this file never reads a raw
/// column back — every fact here reads a value the production seams themselves hand back.
/// </summary>
file sealed class GateRulesTestDatabase : EphemeralStationDatabase
{
    GateRulesTestDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<GateRulesTestDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-gaterules");
        var db = new GateRulesTestDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>
/// Boots the real production composition root (Program.cs) against a real ephemeral Postgres
/// (<see cref="GateRulesTestDatabase"/>) and a real completions-shaped HTTP endpoint
/// (<see cref="LlmCompletionsStub"/>) — mirrors Story345_PaWireProof.cs's own PaWireProofWebFactory,
/// minus the TTS/Kokoro wiring that factory carries: every scenario using this factory stops at the
/// copy writer (no render, no air — see this file's own header remarks), so there is no
/// <c>Tts:Endpoint</c>/<c>Tts:CacheRoot</c> to stand up (Support/LlmCompletionsStub.cs's own
/// LlmCompletionsWebFactory is this exact same "no TTS needed" posture one seam over — the default
/// <c>Tts:CacheRoot</c> ("/tts") is never written to when nothing ever renders).
/// </summary>
file sealed class GateRulesWebFactory(GateRulesTestDatabase db, string llmEndpoint, string stationName)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story364-gate-rules-wire";
    internal const string Model = "test-model-story364";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);

        // The exact four Station:* keys compose.yaml itself overrides in production (Story345_PaWire-
        // Proof.cs's own remarks) — every other Station:* leaf rides appsettings.json's own shipped
        // default, unchanged. Station:Name is now a PARAMETER, not a literal (T352 review round 2,
        // HIGH-1): every arc but TheDriftedStationNameArc passes this file's usual "GWAV 108.8"
        // default; that one arc passes a different name on purpose — see its own remarks.
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", stationName);
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.UseSetting("Llm:Endpoint", llmEndpoint);
        builder.UseSetting("Llm:Model", Model);

        // No Liquidsoap/Kokoro-model/real-background-loop reach during this test — mirrors every
        // other WebApplicationFactory-based spec in this suite.
        builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
    }
}

/// <summary>
/// The no-DB shape LOW-4 calls for (T352 review round 2): mirrors Support/LlmCompletionsStub.cs's own
/// <c>LlmCompletionsWebFactory</c> (Story196/Story353's own precedent — real DI graph, only
/// <see cref="IHostedService"/> removed, no Postgres reach at all) plus the Station:* overrides this
/// file's other arcs already carry, so <see cref="TheLeadInPreviewArc"/>'s own facts stay comparable
/// ("GWAV 108.8" everywhere in this file, not a second station identity for one arc alone).
/// </summary>
file sealed class LeadInPreviewWebFactory(string llmEndpoint) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story364-leadin-preview";
    internal const string Model = "test-model-story364-leadin";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.UseSetting("Llm:Endpoint", llmEndpoint);
        builder.UseSetting("Llm:Model", Model);

        builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
    }
}
