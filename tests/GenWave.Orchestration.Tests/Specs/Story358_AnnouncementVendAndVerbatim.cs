// STORY-358 — The DJ says it: two fidelities, one fallback (SPEC F144.1/.2 · PLAN T341)
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;
using Xunit;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureAnnouncementVendAndVerbatim
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    static Orchestrator BuildOrchestrator(
        FakeAnnouncementSource announcementSource,
        FakeVerbatimSegmentRenderer announcementRenderer,
        FakeTtsSegmentSource tts,
        CadenceConfig? cadence = null,
        FakeTtsVoiceLister? voiceLister = null,
        string stationVoice = "default",
        ILogger<Orchestrator>? logger = null)
    {
        var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", stationVoice));
        var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
        var cadenceProvider = new FakeCadenceProvider(cadence ?? new CadenceConfig
        {
            LeadInBeforeEachTrack = true,
            BackAnnounceAfterEachTrack = true,
            StationIdEveryNUnits = 0,
        });
        var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings());
        var catalog = new FakeMediaCatalog(MakeRef("t1"));
        var musicSelectionPolicy = new MusicSelectionPolicy(catalog, NullLogger<MusicSelectionPolicy>.Instance);

        return new Orchestrator(
            identityProvider, scopeProvider, cadenceProvider, rotationProvider, musicSelectionPolicy, tts,
            new FakeActivePersonaAccessor(), logger ?? NullLogger<Orchestrator>.Instance,
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(5)),
            new SpeechDeferralQueue(TimeProvider.System),
            TimeProvider.System, new FakeBoundaryBiasProvider(TimeSpan.Zero),
            announcementSource: announcementSource,
            announcementRenderer: announcementRenderer,
            voiceLister: voiceLister);
    }

    static MediaReference MakeRef(string id) => new(
        id, $"/media/{id}.mp3", $"Track {id}", new Loudness(-23.0, -1.0, true),
        null, null, null, null, null, null, null, null);

    static CadenceConfig AnnouncementOnlyCadence => new()
    {
        LeadInBeforeEachTrack = false,
        BackAnnounceAfterEachTrack = false,
        StationIdEveryNUnits = 0,
    };

    // -------------------------------------------------------------------------
    // Scenario: vend at unit assembly (STORY-358 AC1)
    // -------------------------------------------------------------------------

    public sealed class ScenarioVendAtUnitAssembly
    {
        readonly List<MediaItem> produced;
        readonly List<MediaItem> announcementsSeen;
        readonly FakeAnnouncementSource announcementSource;

        public ScenarioVendAtUnitAssembly()
        {
            announcementSource = new FakeAnnouncementSource();
            announcementSource.Pending.Enqueue(new AnnouncementItem(101, "First", Verbatim: true, RequestedVoice: null));
            announcementSource.Pending.Enqueue(new AnnouncementItem(102, "Second", Verbatim: true, RequestedVoice: null));
            announcementSource.Pending.Enqueue(new AnnouncementItem(103, "Third", Verbatim: true, RequestedVoice: null));

            var renderer = new FakeVerbatimSegmentRenderer();
            var tts = new FakeTtsSegmentSource();
            var orchestrator = BuildOrchestrator(announcementSource, renderer, tts);
            var ctx = new PlayoutContext([]);

            // Drives units until all 3 seeded announcements have aired, or a generous safety cap is
            // hit — deliberately NOT a fixed call count, so this arrange stays correct even if the
            // Orchestrator's own per-unit item count ever changes for an unrelated reason.
            produced = [];
            announcementsSeen = [];
            for (var i = 0; i < 16 && announcementsSeen.Count < 3; i++)
            {
                var item = orchestrator.GetNextAsync(ctx, CancellationToken.None).GetAwaiter().GetResult();
                if (item is null) continue;
                produced.Add(item);
                if (item.SegmentKind == SegmentKind.Announcement) announcementsSeen.Add(item);
            }
        }

        [Fact]
        public void TheTwoOldestDeliverableAnnouncementsAreClaimedAtomically()
        {
            Assert.True(announcementsSeen.Count >= 2, "expected at least the two oldest to have vended");
            Assert.True(AnnouncementMediaId.TryUnwrap(announcementsSeen[0].MediaId, out var firstId));
            Assert.True(AnnouncementMediaId.TryUnwrap(announcementsSeen[1].MediaId, out var secondId));
            Assert.Equal((101L, 102L), (firstId, secondId));
        }

        [Fact]
        public void AThirdPendingAnnouncementWaitsForTheNextUnit()
        {
            Assert.Equal(3, announcementsSeen.Count);
            Assert.True(AnnouncementMediaId.TryUnwrap(announcementsSeen[2].MediaId, out var thirdId));
            Assert.Equal(103L, thirdId);
            // The cap (2) forced a SECOND claim call to reach the third item — proves it genuinely
            // waited for a later unit's own vend, rather than all three landing in one claim.
            Assert.True(
                announcementSource.ClaimCallCount >= 2,
                "expected the third announcement to require a second claim call, not the first");
        }

        [Fact]
        public void EachVendedAnnouncementBecomesAnAnnouncementKindSegment() =>
            Assert.All(announcementsSeen, i => Assert.Equal(SegmentKind.Announcement, i.SegmentKind));

        [Fact]
        public void TheSegmentIsPlacedAfterTheBackAnnounce()
        {
            var backAnnounceIndex = produced.FindIndex(i => i.SegmentKind == SegmentKind.BackAnnounce);
            Assert.True(backAnnounceIndex >= 0, "expected a back-announce to have aired during this run");

            var announcementAfterIndex = produced.FindIndex(backAnnounceIndex + 1, i => i.SegmentKind == SegmentKind.Announcement);
            Assert.True(announcementAfterIndex > backAnnounceIndex);
        }
    }

    // -------------------------------------------------------------------------
    // Scenario: the verbatim path bypasses the LLM entirely (STORY-358 AC2)
    // -------------------------------------------------------------------------

    public sealed class ScenarioVerbatimBypassesTheLlm
    {
        [Fact]
        public async Task TheExactMessageTextRendersThroughTheTtsPipeline()
        {
            var announcementSource = new FakeAnnouncementSource();
            announcementSource.Pending.Enqueue(
                new AnnouncementItem(201, "The garage sale starts at nine.", Verbatim: true, RequestedVoice: null));
            var renderer = new FakeVerbatimSegmentRenderer();
            var orchestrator = BuildOrchestrator(announcementSource, renderer, new FakeTtsSegmentSource(), AnnouncementOnlyCadence);
            var ctx = new PlayoutContext([]);

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            var call = Assert.Single(renderer.Calls);
            Assert.Equal("The garage sale starts at nine.", call.Copy.Text);
        }

        [Fact]
        public async Task NoLlmCallOccursForAVerbatimAnnouncement()
        {
            var announcementSource = new FakeAnnouncementSource();
            announcementSource.Pending.Enqueue(new AnnouncementItem(202, "Message", Verbatim: true, RequestedVoice: null));
            var renderer = new FakeVerbatimSegmentRenderer();
            // `tts` stands in for the ordinary ISegmentCopyWriter-backed pipeline (the ONLY seam an
            // LLM copy writer could ever sit behind, production-side) — asserting it is never called
            // for this run proves no LLM-authoring path was ever reached for the announcement.
            var tts = new FakeTtsSegmentSource();
            var orchestrator = BuildOrchestrator(announcementSource, renderer, tts, AnnouncementOnlyCadence);
            var ctx = new PlayoutContext([]);

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.Equal(0, tts.RenderCallCount);
        }

        [Fact]
        public async Task ARequestedVoiceIsHonoredWhenKnown()
        {
            var announcementSource = new FakeAnnouncementSource();
            announcementSource.Pending.Enqueue(new AnnouncementItem(203, "Message", Verbatim: true, RequestedVoice: "nova"));
            var renderer = new FakeVerbatimSegmentRenderer();
            var voiceLister = new FakeTtsVoiceLister { KnownVoices = ["nova", "shimmer"] };
            var orchestrator = BuildOrchestrator(
                announcementSource, renderer, new FakeTtsSegmentSource(), AnnouncementOnlyCadence, voiceLister);
            var ctx = new PlayoutContext([]);

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            var call = Assert.Single(renderer.Calls);
            Assert.Equal("nova", call.Request.Voice);
        }

        [Fact]
        public async Task AnUnknownRequestedVoiceFallsBackToTheStationVoice()
        {
            var announcementSource = new FakeAnnouncementSource();
            announcementSource.Pending.Enqueue(
                new AnnouncementItem(204, "Message", Verbatim: true, RequestedVoice: "not-a-real-voice"));
            var renderer = new FakeVerbatimSegmentRenderer();
            var voiceLister = new FakeTtsVoiceLister { KnownVoices = ["nova", "shimmer"] };
            var orchestrator = BuildOrchestrator(
                announcementSource, renderer, new FakeTtsSegmentSource(), AnnouncementOnlyCadence, voiceLister,
                stationVoice: "default");
            var ctx = new PlayoutContext([]);

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            var call = Assert.Single(renderer.Calls);
            Assert.Equal("default", call.Request.Voice);
        }
    }

    // -------------------------------------------------------------------------
    // Scenario: SPEC F145.2's defense-in-depth vend refusal (STORY-359 AC2)
    // -------------------------------------------------------------------------

    public sealed class ScenarioTheVendRefusesWhilePublic
    {
        [Fact]
        public async Task NoAnnouncementVendsWhileSpectatorModeIsOn()
        {
            // FakeAnnouncementSource.RefuseVend stands in for the real SPEC F145.2 SpectatorMode
            // refusal, which lives behind a Host-side decorator this test assembly cannot reach (see
            // that fake's own remarks) — from the Orchestrator's own vantage point, a refused claim
            // and a genuinely empty one are indistinguishable, which is the whole point of F145.2's
            // "the Orchestrator never reads privacy state" ruling.
            var announcementSource = new FakeAnnouncementSource { RefuseVend = true };
            announcementSource.Pending.Enqueue(new AnnouncementItem(401, "Message", Verbatim: true, RequestedVoice: null));
            var renderer = new FakeVerbatimSegmentRenderer();
            var tts = new FakeTtsSegmentSource();
            var cadence = new CadenceConfig
            {
                LeadInBeforeEachTrack = true,
                BackAnnounceAfterEachTrack = false,
                StationIdEveryNUnits = 0,
            };
            var orchestrator = BuildOrchestrator(announcementSource, renderer, tts, cadence);
            var ctx = new PlayoutContext([]);

            var produced = new List<MediaItem>();
            for (var i = 0; i < 4; i++)
            {
                var item = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
                if (item is not null) produced.Add(item);
            }

            Assert.DoesNotContain(produced, i => i.SegmentKind == SegmentKind.Announcement);
        }
    }

    // -------------------------------------------------------------------------
    // Scenario: the announcement id is recoverable from the MediaId (PLAN T341 extension)
    // -------------------------------------------------------------------------

    public sealed class ScenarioTheAnnouncementIdIsRecoverable
    {
        [Fact]
        public async Task TheAnnouncementIdSurvivesOntoTheRenderedSegmentsMediaId()
        {
            var announcementSource = new FakeAnnouncementSource();
            announcementSource.Pending.Enqueue(new AnnouncementItem(555, "Message", Verbatim: true, RequestedVoice: null));
            var renderer = new FakeVerbatimSegmentRenderer();
            var orchestrator = BuildOrchestrator(announcementSource, renderer, new FakeTtsSegmentSource(), AnnouncementOnlyCadence);
            var ctx = new PlayoutContext([]);

            var aired = await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.NotNull(aired);
            Assert.True(AnnouncementMediaId.TryUnwrap(aired.MediaId, out var recoveredId));
            Assert.Equal(555L, recoveredId);
        }
    }

    // -------------------------------------------------------------------------
    // Scenario: the caller-side half of the max<=0 clamp contract (PLAN T341 extension)
    // -------------------------------------------------------------------------

    public sealed class ScenarioTheVendCeilingIsNeverNonPositive
    {
        [Fact]
        public async Task TheOrchestratorsOwnVendCeilingIsAlwaysPositive()
        {
            // The T338 review carry-forward requires max<=0 to be clamped AT THE IMPLEMENTATION
            // (AnnouncementRepository.ClaimDeliverableAsync, MediaLibrary — proven against the real
            // Postgres-backed repository by the T341 build's own smoke test, since Orchestration.Tests
            // cannot reference MediaLibrary at all). This fact pins the OTHER half: the Orchestrator's
            // own vend ceiling is a fixed positive constant, so the callee's defensive clamp never has
            // reason to fire in practice — a future refactor that computed the cap dynamically instead
            // could otherwise silently regress it to zero or negative with nothing here to catch it.
            var announcementSource = new FakeAnnouncementSource();
            announcementSource.Pending.Enqueue(new AnnouncementItem(601, "Message", Verbatim: true, RequestedVoice: null));
            var renderer = new FakeVerbatimSegmentRenderer();
            var orchestrator = BuildOrchestrator(announcementSource, renderer, new FakeTtsSegmentSource(), AnnouncementOnlyCadence);
            var ctx = new PlayoutContext([]);

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            Assert.NotEmpty(announcementSource.MaxRequested);
            Assert.All(announcementSource.MaxRequested, max => Assert.True(max > 0, $"expected a positive vend ceiling, got {max}"));
        }
    }

    // -------------------------------------------------------------------------
    // Scenario: the T338 FreshPerAiring:true contract, pinned from the producing side (PLAN T341 extension)
    // -------------------------------------------------------------------------

    public sealed class ScenarioTheProducedCopyIsFreshPerAiring
    {
        [Fact]
        public async Task TheCopyHandedToTheRendererIsFreshPerAiring()
        {
            // TtsSegmentSource's own drop guard (SPEC F144.2/F144.4) treats copy.FreshPerAiring as
            // the ONE test separating genuine owner content from inert template floor text — pinned
            // here on the PRODUCING side (the Orchestrator's own construction of the SegmentCopy it
            // hands to IVerbatimSegmentRenderer), complementing TtsSegmentSource's own consuming-side
            // pin (GenWave.Tts.Tests).
            var announcementSource = new FakeAnnouncementSource();
            announcementSource.Pending.Enqueue(new AnnouncementItem(701, "Message", Verbatim: true, RequestedVoice: null));
            var renderer = new FakeVerbatimSegmentRenderer();
            var orchestrator = BuildOrchestrator(announcementSource, renderer, new FakeTtsSegmentSource(), AnnouncementOnlyCadence);
            var ctx = new PlayoutContext([]);

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            var call = Assert.Single(renderer.Calls);
            Assert.True(call.Copy.FreshPerAiring);
        }
    }

    // -------------------------------------------------------------------------
    // Scenario: a faulted claim never costs the whole unit (T341 review finding F1)
    // -------------------------------------------------------------------------

    public sealed class ScenarioAnnouncementClaimFaultIsolation
    {
        [Fact]
        public async Task AClaimFaultStillProducesTheUnitsMusicItemAndLogsAWarn()
        {
            // A claim fault (a Host-side decorator's own DB round trip or options read, either of
            // which can throw) must degrade to an empty claim, mirroring ResolveAnnouncementVoiceAsync's
            // own SPEC F12.4 posture (SAME try/catch shape) — never fault unit assembly itself.
            var announcementSource = new FakeAnnouncementSource { Throw = true };
            var renderer = new FakeVerbatimSegmentRenderer();
            var tts = new FakeTtsSegmentSource();
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(
                announcementSource, renderer, tts, AnnouncementOnlyCadence, logger: logger);
            var ctx = new PlayoutContext([]);

            var item = await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            // The unit still produces its music item — no Announcement segment reached the buffer —
            // and a WARN names the fault rather than it silently vanishing.
            Assert.NotNull(item);
            Assert.Equal("t1", item.MediaId);
            Assert.Null(item.SegmentKind);
            Assert.Contains(
                logger.Warnings,
                w => w.Contains("Announcement claim failed", StringComparison.Ordinal));
        }
    }

    // -------------------------------------------------------------------------
    // Scenario: a flavored announcement takes the SAME verbatim path today (SPEC F144.3, T341 review
    // finding F4 — the T342 handoff pinned)
    // -------------------------------------------------------------------------

    public sealed class ScenarioFlavoredAnnouncementsTakeTheVerbatimPathToo
    {
        [Fact]
        public async Task AFlavoredAnnouncementStillRendersTheExactTextWithNoLlmCall()
        {
            var announcementSource = new FakeAnnouncementSource();
            announcementSource.Pending.Enqueue(
                new AnnouncementItem(801, "The garage sale starts at nine.", Verbatim: false, RequestedVoice: null));
            var renderer = new FakeVerbatimSegmentRenderer();
            // `tts` stands in for the ordinary ISegmentCopyWriter-backed pipeline — the ONLY seam an
            // LLM/DJ-flavor copy writer could ever sit behind, production-side (T342 has not landed
            // it yet) — asserting it is never called proves Verbatim:false takes the SAME LLM-free
            // path as Verbatim:true today.
            var tts = new FakeTtsSegmentSource();
            var orchestrator = BuildOrchestrator(announcementSource, renderer, tts, AnnouncementOnlyCadence);
            var ctx = new PlayoutContext([]);

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            var call = Assert.Single(renderer.Calls);
            Assert.Equal("The garage sale starts at nine.", call.Copy.Text);
            Assert.Equal(0, tts.RenderCallCount);
        }
    }

    // -------------------------------------------------------------------------
    // Scenario: the voice registry and renderer's own degrade knobs (T341 review finding F6 — both
    // knobs already existed on their fakes, unused by any prior fact; F8's id-naming carried into
    // the drop-WARN fact below)
    // -------------------------------------------------------------------------

    public sealed class ScenarioVoiceRegistryAndRendererFaultsDegradeGracefully
    {
        [Fact]
        public async Task AnUnreachableVoiceRegistryVendsOnTheStationVoiceAndWarns()
        {
            var announcementSource = new FakeAnnouncementSource();
            announcementSource.Pending.Enqueue(
                new AnnouncementItem(501, "Message", Verbatim: true, RequestedVoice: "nova"));
            var renderer = new FakeVerbatimSegmentRenderer();
            var voiceLister = new FakeTtsVoiceLister { Throw = true };
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(
                announcementSource, renderer, new FakeTtsSegmentSource(), AnnouncementOnlyCadence, voiceLister,
                stationVoice: "default", logger: logger);
            var ctx = new PlayoutContext([]);

            await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            var call = Assert.Single(renderer.Calls);
            Assert.Equal("default", call.Request.Voice);
            Assert.Contains(
                logger.Warnings,
                w => w.Contains("voice registry unreachable", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task ARendererReturningNullDropsTheAnnouncementNamingItsIdAndMusicStillAirs()
        {
            var announcementSource = new FakeAnnouncementSource();
            announcementSource.Pending.Enqueue(new AnnouncementItem(502, "Message", Verbatim: true, RequestedVoice: null));
            var renderer = new FakeVerbatimSegmentRenderer { AlwaysReturnNull = true };
            var tts = new FakeTtsSegmentSource();
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(
                announcementSource, renderer, tts, AnnouncementOnlyCadence, logger: logger);
            var ctx = new PlayoutContext([]);

            var item = await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            // Music still airs — the dropped announcement never costs the unit — and the drop WARN
            // names WHICH claimed row dropped (SPEC F144.5, T341 review finding F8), not merely "an"
            // announcement.
            Assert.NotNull(item);
            Assert.Equal("t1", item.MediaId);
            Assert.Contains(
                logger.Warnings,
                w => w.Contains("502", StringComparison.Ordinal) && w.Contains("dropped", StringComparison.OrdinalIgnoreCase));
        }
    }
}
