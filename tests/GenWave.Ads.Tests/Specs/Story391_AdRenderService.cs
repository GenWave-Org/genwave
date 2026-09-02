// STORY-391 — AdRenderService's own render flow (T401 review F1: real RenderAsync coverage,
// mutant-killing on the insert shape / ceiling formula / confirm-calls-MarkReady, plus every
// TryMarkFailedAsync edge and the F2 voice-plan fail-safe). The assembler half (AC1/AC2/AC3/AC5)
// lives in GenWave.Tts.Tests/Specs/Story391_AdRenderAssembly.cs; the worker half (AC4/AC6) in
// GenWave.Ads.Tests/Specs/Story391_AdSpotWorker.cs (still T402's, untouched here).

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Fakes;
using GenWave.Core.Domain;
using GenWave.Tts;

public static class FeatureAdRenderService
{
    const string StationName = "GWAV Test Station";
    const string StationVoice = "station_voice";
    const string TwoLineScript = "ANNOUNCER: Come on down to the big sale.\nVOICE1: Prices you won't believe.";

    static AdSpot MakeSpot(
        long id = 1,
        string script = TwoLineScript,
        int spotSeconds = 30,
        string? voicePlan = null,
        long? bedMediaId = null,
        string title = "Big Sale Spot") =>
        new(
            Id: id, Brand: "Acme", Title: title, Brief: null, Script: script, Source: AdSource.Llm,
            PackSlug: null, SpotSeconds: spotSeconds, VoicePlan: voicePlan, BedMediaId: bedMediaId,
            State: AdState.Rendering, FailReason: null, MediaId: null, Generation: 1,
            CreatedAt: DateTime.UtcNow, StateChangedAt: DateTime.UtcNow, RenderedAt: null,
            RetiredAt: null, Version: "1");

    /// <summary>Wires a REAL <see cref="AdRenderService"/> against fakes at every I/O seam — the
    /// SAME "real subject, faked edges" posture every other spec in this suite uses. Seeds the ads
    /// library by default (<paramref name="seedAdsLibrary"/> = false pins the "library missing"
    /// failure path).</summary>
    static (AdRenderService Service, FakeCastSegmentAuthor Author, FakeAdSpotStore Store,
        FakeAdminMediaLookup AdminLookup, FakeAdsLibraryStore Libraries, long AdsLibraryId) Build(
            bool seedAdsLibrary = true, double toleranceRatio = 0.4, double bedDuckDb = -12.0)
    {
        var author = new FakeCastSegmentAuthor();
        var store = new FakeAdSpotStore();
        var adminLookup = new FakeAdminMediaLookup();
        var libraries = new FakeAdsLibraryStore();
        var adsLibraryId = seedAdsLibrary ? libraries.AddExisting("ads") : -1;
        var stationIdentity = new FakeStationIdentityProvider(new StationIdentity("station-1", StationName, StationVoice));
        var adsOptions = new FakeOptionsMonitor<AdsOptions>(
            new AdsOptions { LibraryName = "ads", DurationToleranceRatio = toleranceRatio, BedDuckDb = bedDuckDb });
        var locatorRoots = new AdSpotLocatorRoots("/media", "/authored");

        var service = new AdRenderService(
            author, store, adminLookup, libraries, stationIdentity, adsOptions, locatorRoots,
            new NoOpLogger<AdRenderService>());

        return (service, author, store, adminLookup, libraries, adsLibraryId);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the three mutant-killing facts (T401 review F1)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheAuthoredInsertShape
    {
        [Fact]
        public async Task CarriesAdKindTheAdsLibraryIdAndStationTags()
        {
            // Mutant 1 (Kind→Liner at AdRenderService.cs:231, ads air silently as liners): the
            // PRODUCTION BuildInsert closure is invoked for real by FakeCastSegmentAuthor, never a
            // test-local lookalike.
            var (service, author, _, _, _, adsLibraryId) = Build();

            await service.RenderAsync(MakeSpot(), CancellationToken.None);

            var insert = author.CapturedInsert;
            Assert.NotNull(insert);
            Assert.Equal(ImagingKind.Ad, insert!.Kind);
            Assert.Equal(adsLibraryId, insert.LibraryId);
            Assert.Equal(StationName, insert.Tags.Artist);
            Assert.Equal("Big Sale Spot", insert.Tags.Title);
        }
    }

    public sealed class ScenarioTheCeilingFormula
    {
        [Fact]
        public async Task IsSpotSecondsTimesOnePlusTheToleranceRatio()
        {
            // Mutant 2 (ceiling→3600 at AdRenderService.cs:100): asserts the EXACT computed value,
            // not merely "some ceiling was set" — a hardcoded/wrong constant fails this.
            var (service, author, _, _, _, _) = Build(toleranceRatio: 0.4);

            await service.RenderAsync(MakeSpot(spotSeconds: 30), CancellationToken.None);

            Assert.NotNull(author.LastRequest);
            Assert.Equal(42.0, author.LastRequest!.CeilingSeconds, precision: 6);
        }
    }

    public sealed class ScenarioTheConfirmation
    {
        [Fact]
        public async Task CallsMarkReadyWithTheSpotIdAndTheMediaId()
        {
            // Mutant 3 (confirm→FromResult(true) at AdRenderService.cs:108, MarkReadyAsync never
            // called): FakeCastSegmentAuthor genuinely INVOKES the confirmAsync closure it was
            // handed — a stub that always returns true would leave FakeAdSpotStore untouched.
            var (service, author, store, _, _, _) = Build();
            author.MediaIdToConfirm = 999;
            var spot = MakeSpot(id: 55);

            await service.RenderAsync(spot, CancellationToken.None);

            Assert.Equal(1, store.MarkReadyCalls);
            Assert.Equal(55, store.LastMarkReadySpotId);
            Assert.Equal(999, store.LastMarkReadyMediaId);
            Assert.Equal(0, store.MarkFailedCalls);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the voice plan, once well-formed, is genuinely HONORED (T401 round-2 review F12)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheVoicePlanIsHonored
    {
        [Fact]
        public async Task AWellFormedPlanCastsEachTagItsOwnVoiceAndPace()
        {
            // T401 round-2 review F12: the F2 fail-safe facts only ever proved a MALFORMED/absent
            // plan degrades safely — none of them proved a WELL-FORMED plan is actually READ. A
            // "var plan = null" mutant (voice casting amputated, every spot rendered in one voice)
            // would survive every fail-safe fact untouched; this one kills it.
            var (service, author, store, _, _, _) = Build();
            var spot = MakeSpot(voicePlan: """[{"tag":"ANNOUNCER","voiceId":"announcer_v","pace":1.2},{"tag":"VOICE1","voiceId":"other_v"}]""");

            await service.RenderAsync(spot, CancellationToken.None);

            Assert.Equal(0, store.MarkFailedCalls);
            Assert.NotNull(author.LastRequest);
            var announcer = author.LastRequest!.Cast.Single(member => member.Tag == "ANNOUNCER");
            var voice1 = author.LastRequest.Cast.Single(member => member.Tag == "VOICE1");

            Assert.Equal("announcer_v", announcer.Voice.VoiceId);
            Assert.Equal(1.2, announcer.Voice.Pace);
            Assert.Equal("other_v", voice1.Voice.VoiceId);
            Assert.Equal(1.0, voice1.Voice.Pace); // AdVoicePlanEntry.Pace's own default when omitted.

            Assert.NotEqual(StationVoice, announcer.Voice.VoiceId);
            Assert.NotEqual(StationVoice, voice1.Voice.VoiceId);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the F2 voice-plan fail-safe (never the refusal the design forbids)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheVoicePlanFailSafe
    {
        [Fact]
        public async Task AnAbsentPlanDefaultsEveryTagToTheStationVoice()
        {
            var (service, author, store, _, _, _) = Build();

            await service.RenderAsync(MakeSpot(voicePlan: null), CancellationToken.None);

            Assert.Equal(0, store.MarkFailedCalls);
            Assert.NotNull(author.LastRequest);
            Assert.All(author.LastRequest!.Cast, member => Assert.Equal(StationVoice, member.Voice.VoiceId));
        }

        [Fact]
        public async Task AnEntryMissingTagIsDroppedNeverThrown()
        {
            // T401 review F2: [{"voiceId":"x"}] deserializes with a null Tag (System.Text.Json
            // passes null for a missing constructor-bound property regardless of this record's own
            // non-nullable annotation) — an unfiltered null Tag throws ArgumentNullException out of
            // ResolveCast's own Dictionary<string,_> key, failing the spot outright: the exact
            // refusal SPEC F161.2's "null plan is legal" forbids.
            var (service, author, store, _, _, _) = Build();
            var spot = MakeSpot(voicePlan: """[{"voiceId":"some_voice"}]""");

            await service.RenderAsync(spot, CancellationToken.None);

            Assert.Equal(0, store.MarkFailedCalls);
            Assert.NotNull(author.LastRequest);
            Assert.All(author.LastRequest!.Cast, member => Assert.Equal(StationVoice, member.Voice.VoiceId));
        }

        [Fact]
        public async Task AnEntryMissingVoiceIdIsDroppedNeverSentToTheSynth()
        {
            // T401 review F2's other shape: [{"tag":"ANNOUNCER"}] with a null VoiceId — dropped, not
            // forwarded as a literal null voice id.
            var (service, author, store, _, _, _) = Build();
            var spot = MakeSpot(voicePlan: """[{"tag":"ANNOUNCER"}]""");

            await service.RenderAsync(spot, CancellationToken.None);

            Assert.Equal(0, store.MarkFailedCalls);
            Assert.NotNull(author.LastRequest);
            var announcer = author.LastRequest!.Cast.Single(member => member.Tag == "ANNOUNCER");
            Assert.Equal(StationVoice, announcer.Voice.VoiceId);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — every TryMarkFailedAsync edge
    // ---------------------------------------------------------------------

    public sealed class ScenarioEveryFailureEdgeReportsThroughMarkFailed
    {
        [Fact]
        public async Task AnUnparseableScriptFailsWithoutEverReachingTheAuthor()
        {
            var (service, author, store, _, _, _) = Build();
            var spot = MakeSpot(id: 2, script: "not a valid script at all");

            await service.RenderAsync(spot, CancellationToken.None);

            Assert.Equal(1, store.MarkFailedCalls);
            Assert.Equal(2, store.LastMarkFailedSpotId);
            Assert.Contains("no longer parses", store.LastMarkFailedReason, StringComparison.Ordinal);
            Assert.Null(author.LastRequest);
        }

        [Fact]
        public async Task AnUnknownBedMediaFailsWithoutEverReachingTheAuthor()
        {
            var (service, author, store, _, _, _) = Build();
            var spot = MakeSpot(id: 3, bedMediaId: 777); // never added to FakeAdminMediaLookup

            await service.RenderAsync(spot, CancellationToken.None);

            Assert.Equal(1, store.MarkFailedCalls);
            Assert.Equal(3, store.LastMarkFailedSpotId);
            Assert.Contains("unknown bed media", store.LastMarkFailedReason, StringComparison.Ordinal);
            Assert.Null(author.LastRequest);
        }

        [Fact]
        public async Task AMissingAdsLibraryFailsWithoutEverReachingTheAuthor()
        {
            var (service, author, store, _, _, _) = Build(seedAdsLibrary: false);
            var spot = MakeSpot(id: 4);

            await service.RenderAsync(spot, CancellationToken.None);

            Assert.Equal(1, store.MarkFailedCalls);
            Assert.Equal(4, store.LastMarkFailedSpotId);
            Assert.Contains("ads library does not exist", store.LastMarkFailedReason, StringComparison.Ordinal);
            Assert.Null(author.LastRequest);
        }

        [Fact]
        public async Task AnAuthorFailurePinsTheTypedReasonInTheFailReason()
        {
            // The exact reason text an operator would see on the spot's own fail_reason column —
            // "render: ConfirmationFailed" pinned literally, per the review.
            var (service, author, store, _, _, _) = Build();
            author.InvokeDelegates = false;
            author.Result = CastSegmentAuthorResult.Failure(CastSegmentFailureReason.ConfirmationFailed, "confirmation declined");
            var spot = MakeSpot(id: 9);

            await service.RenderAsync(spot, CancellationToken.None);

            Assert.Equal(1, store.MarkFailedCalls);
            Assert.Equal(9, store.LastMarkFailedSpotId);
            Assert.Contains("render: ConfirmationFailed", store.LastMarkFailedReason, StringComparison.Ordinal);
            Assert.Contains("confirmation declined", store.LastMarkFailedReason, StringComparison.Ordinal);
        }
    }
}
