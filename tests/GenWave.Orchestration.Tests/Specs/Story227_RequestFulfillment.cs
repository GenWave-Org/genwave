// STORY-227 — The request line jumps the queue, within the law (SPEC F87.6, PLAN T90)
//
// BDD specification — xUnit. Three seams are proven, each with the double that can actually observe
// it (a scripted fake IRequestFulfillmentSource is WRONG for the provider's own state/event
// assertions — it would test nothing): FeatureOrchestratorConsultationOrder drives the REAL
// Orchestrator with a SCRIPTED IRequestFulfillmentSource to prove the consultation order/short-circuit
// wiring; FeatureSingleConsultationPerPick drives the REAL Orchestrator AND the REAL
// RequestFulfillmentProvider together (against FakeRequestStore/FakeRequestCatalogProbe/
// CapturingStationEventSink) to prove the PLAN T90 review fix — the fulfillment rung runs exactly
// ONCE per pick even when the boundary-bias resample loop is active, never once per resample;
// FeatureRequestFulfillmentProvider drives the REAL RequestFulfillmentProvider alone to prove its own
// one-shot/TTL/override/veto logic.

using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureOrchestratorConsultationOrder
{
    static MediaReference MakeRef(string id) => new(
        MediaId: id,
        Locator: $"/media/{id}.mp3",
        Title: $"Track {id}",
        Loudness: new Loudness(-23.0, -1.0, true),
        DurationMs: null,
        SampleRate: null,
        Channels: null,
        BitrateKbps: null,
        Artist: null,
        Album: null,
        Genre: null,
        Year: null);

    static CadenceConfig SilentCadence => new()
    {
        LeadInBeforeEachTrack = false,
        BackAnnounceAfterEachTrack = false,
        StationIdEveryNUnits = 0,
    };

    static Orchestrator BuildOrchestrator(
        IRequestFulfillmentSource requestFulfillmentSource, IPersonaPickProvider personaPickProvider)
    {
        var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default"));
        var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
        var cadenceProvider = new FakeCadenceProvider(SilentCadence);
        var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings());
        var envelope = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, [], EnergyRange.Unconstrained);

        // An empty catalog: if the fulfillment rung were NOT consulted first (or its result were
        // ignored), both the persona rung and the envelope-only ladder would find nothing here — so a
        // non-null pick matching the fulfillment's own media id is only possible via the short-circuit
        // this feature proves.
        var catalog = new FakeMediaCatalog(null);

        return new Orchestrator(
            identityProvider, scopeProvider, cadenceProvider, rotationProvider, catalog,
            new FakeTtsSegmentSource(), new FakeActivePersonaAccessor(), new CapturingLogger<Orchestrator>(),
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(5)),
            new SpeechDeferralQueue(TimeProvider.System),
            TimeProvider.System, new FakeBoundaryBiasProvider(TimeSpan.Zero),
            new FakeEnvelopeProvider(envelope),
            personaPickProvider,
            requestFulfillmentSource);
    }

    public static class ScenarioALiveRequestShortCircuitsTheChain
    {
        [Fact]
        public static async Task APendingMatchedRequestWinsThePickAheadOfThePersonaRung()
        {
            // Arrange: a fulfillment source scripted to hand back a specific track, and a persona
            // provider that would hand back a DIFFERENT one if it were ever consulted.
            var fulfilled = new RotationCandidate(MakeRef("requested"), false, false, RequestFulfilled: true);
            var fulfillmentSource = new FakeRequestFulfillmentSource
            {
                NextResult = new RequestFulfillment(fulfilled, RequestId: 42, WasVibe: false),
            };
            var personaPickProvider = new FakePersonaPickProvider { NextResult = new RotationCandidate(MakeRef("persona-picked"), false, false) };
            var orchestrator = BuildOrchestrator(fulfillmentSource, personaPickProvider);

            // Act: pull the next item.
            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Assert: the fulfilled track aired, not the persona rung's own pick — provable only if
            // the fulfillment rung ran BEFORE (and instead of) the persona rung.
            Assert.Equal("requested", item?.MediaId);
        }
    }
}

public static class FeatureSingleConsultationPerPick
{
    // PLAN T90 review regression: the fulfillment rung used to live inside
    // SelectEnvelopeAwareCandidateAsync, which SelectMusicCandidateAsync's boundary-bias loop
    // (SPEC F74.3, STORY-198) re-invokes up to BoundarySampleAttempts times per pick whenever
    // something is due strictly in the future within the bias lookahead window. Driving that window
    // ACTIVE here — with the REAL RequestFulfillmentProvider and TWO live pending requests in
    // FakeRequestStore — is the only way to observe the bug's two symptoms: (a) the buggy loop
    // stamped/published BOTH rows across its repeat samples instead of one, and (b) the FIRST
    // (correctly stamped) request's own track could still lose the final timing comparison to an
    // unrelated catalog pick and never air, despite being narrated fulfilled. A scripted
    // IRequestFulfillmentSource returning the same canned result on every call cannot reproduce
    // either — hence the real provider here, unlike FeatureOrchestratorConsultationOrder above.

    static readonly DateTimeOffset ClockStart = DateTimeOffset.Parse("2030-01-01T00:00:00Z");

    static MediaReference MakeRequestedRef(string id) => new(
        MediaId: id,
        Locator: $"/media/{id}.mp3",
        Title: $"Track {id}",
        Loudness: new Loudness(-23.0, -1.0, true),
        DurationMs: null,
        SampleRate: null,
        Channels: null,
        BitrateKbps: null,
        Artist: null,
        Album: null,
        Genre: null,
        Year: null);

    static MediaReference MakeSamplerTrack(string id, TimeSpan duration) => new(
        MediaId: id,
        Locator: $"/media/{id}.mp3",
        Title: $"Track {id}",
        Loudness: new Loudness(-23.0, -1.0, true),
        DurationMs: (int)duration.TotalMilliseconds,
        SampleRate: null,
        Channels: null,
        BitrateKbps: null,
        Artist: null,
        Album: null,
        Genre: null,
        Year: null);

    static CadenceConfig SilentCadence => new()
    {
        LeadInBeforeEachTrack = false,
        BackAnnounceAfterEachTrack = false,
        StationIdEveryNUnits = 0,
    };

    public static class ScenarioBoundaryBiasActiveWithTwoPendingRequests
    {
        [Fact]
        public static async Task ExactlyOneRequestIsFulfilledAndItsTrackWinsThePick()
        {
            // Arrange: a station-id deferral due in 3 minutes, inside a 10-minute bias lookahead —
            // the same idiom Story198's ScenarioBiasNearDeadline uses to force
            // SelectMusicCandidateAsync's resample loop to actually run (up to
            // BoundarySampleAttempts times) instead of taking the single-call path.
            var clock = new FakeTimeProvider(ClockStart);
            var deferralQueue = new SpeechDeferralQueue(clock);
            deferralQueue.Enqueue(
                SpeechDeferralKind.StationId, "test: due in 3 minutes",
                clock.GetUtcNow() + TimeSpan.FromMinutes(3));

            // Two live pending matched requests, oldest-first by ReceivedAt — proves the fix
            // consults the fulfillment rung exactly ONCE per pick rather than once per resample
            // (which would let the loop drain BOTH rows across its up-to-5 attempts).
            var store = new FakeRequestStore();
            var firstId = store.AddPending(ClockStart.AddMinutes(10), matchedMediaId: 1, receivedAt: ClockStart);
            var secondId = store.AddPending(ClockStart.AddMinutes(10), matchedMediaId: 2, receivedAt: ClockStart.AddSeconds(1));
            var probe = new FakeRequestCatalogProbe
            {
                OnGetSelectableById = (mediaId, _) =>
                    MakeRequestedRef(mediaId == 1 ? "requested-first" : "requested-second"),
            };
            var events = new CapturingStationEventSink();
            var fulfillmentSource = new RequestFulfillmentProvider(
                store, probe, new FakeRequestOverrideEnvelopeProvider(true), events, clock);

            // A catalog pool the sampler would fall through to if (and only if) the fix regressed
            // and the short-circuit stopped winning outright — never touched while the fix holds,
            // since the fulfillment rung now returns before the due/bias branch is even evaluated.
            var catalog = FakeMediaCatalog.WithPool(
            [
                MakeSamplerTrack("three-min", TimeSpan.FromMinutes(3)),
                MakeSamplerTrack("nine-min", TimeSpan.FromMinutes(9)),
            ]);

            var envelope = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, [], EnergyRange.Unconstrained);
            var orchestrator = new Orchestrator(
                new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default")),
                new FakeStationScopeProvider(new LibraryScope([1L])),
                new FakeCadenceProvider(SilentCadence),
                new FakeRotationSettingsProvider(new RotationSettings()),
                catalog,
                new FakeTtsSegmentSource(),
                new FakeActivePersonaAccessor(),
                new CapturingLogger<Orchestrator>(),
                new FakeRenderBudgetProvider(TimeSpan.FromSeconds(5)),
                deferralQueue,
                clock,
                new FakeBoundaryBiasProvider(TimeSpan.FromMinutes(10)),
                new FakeEnvelopeProvider(envelope),
                personaPickProvider: null,
                requestFulfillmentSource: fulfillmentSource);

            // Act: pull the next item — the bias window is active, so pre-fix this drove the
            // resample loop straight through the fulfillment rung on every attempt.
            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Assert: the FIRST (oldest) request's own track won the pick outright — the
            // short-circuit beat the sampler entirely, never subjected to its timing comparison.
            Assert.Equal("requested-first", item?.MediaId);

            // Assert: exactly one request left "pending" — the second was never touched.
            Assert.Equal("fulfilled", store.StatusOf(firstId));
            Assert.Equal("pending", store.StatusOf(secondId));

            // Assert: exactly one RequestFulfilled event published for this single pick.
            Assert.Single(events.Events.OfType<RequestFulfilled>());
        }
    }
}

public static class FeatureRequestFulfillmentProvider
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    static readonly DateTimeOffset Now = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    static readonly SegmentEnvelope Envelope =
        new(TimeOnly.MinValue, TimeOnly.MaxValue, ["Rock"], new EnergyRange(0.2, 0.8));

    static MediaReference MakeRef(string id) => new(
        MediaId: id,
        Locator: $"/media/{id}.mp3",
        Title: $"Track {id}",
        Loudness: new Loudness(-23.0, -1.0, true),
        DurationMs: null,
        SampleRate: null,
        Channels: null,
        BitrateKbps: null,
        Artist: null,
        Album: null,
        Genre: null,
        Year: null);

    static RequestFulfillmentProvider BuildProvider(
        FakeRequestStore store, FakeRequestCatalogProbe probe, bool overrideEnvelope) =>
        new(store, probe, new FakeRequestOverrideEnvelopeProvider(overrideEnvelope),
            NoOpStationEventSink.Instance, new FakeTimeProvider(Now));

    // -------------------------------------------------------------------------
    // HAPPY PATH
    // -------------------------------------------------------------------------

    public static class ScenarioOneShot
    {
        [Fact]
        public static async Task AFulfilledRequestNeverInfluencesALaterPick()
        {
            // Arrange: one pending matched request, freely selectable.
            var media = MakeRef("t1");
            var store = new FakeRequestStore();
            store.AddPending(Now.AddMinutes(10), matchedMediaId: 1);
            var probe = new FakeRequestCatalogProbe { OnGetSelectableById = (_, _) => media };
            var provider = BuildProvider(store, probe, overrideEnvelope: true);

            // Act: fulfill it once, then try again on a later pick.
            var first = await provider.TryFulfillAsync(Envelope, CancellationToken.None);
            Assert.NotNull(first);
            var second = await provider.TryFulfillAsync(Envelope, CancellationToken.None);

            // Assert: the second attempt finds nothing left to fulfill — the one-shot stamp held.
            Assert.Null(second);
        }
    }

    public static class ScenarioOverrideTrue
    {
        [Fact]
        public static async Task OverrideTrueBypassesEnvelopeAndRotationRecency()
        {
            // Arrange: the probe only returns a hit when it was called with NO envelope — proving
            // OverrideEnvelope=true bypassed the genre/energy leg entirely (rotation-recency is never
            // even a parameter to this seam, in either mode — see RequestFulfillmentProvider's remarks).
            var media = MakeRef("t1");
            var store = new FakeRequestStore();
            store.AddPending(Now.AddMinutes(10), matchedMediaId: 1);
            var probe = new FakeRequestCatalogProbe { OnGetSelectableById = (_, env) => env is null ? media : null };
            var provider = BuildProvider(store, probe, overrideEnvelope: true);

            // Act.
            var result = await provider.TryFulfillAsync(Envelope, CancellationToken.None);

            // Assert: the bypass-only-reachable candidate came back.
            Assert.Equal(media.MediaId, result?.Candidate.Media.MediaId);
        }
    }

    public static class ScenarioVibeRequest
    {
        [Fact]
        public static async Task AVibeRequestConstrainsExactlyOnePickThroughTheMoodMachinery()
        {
            // Arrange: a moods-only (unmatched) pending request; the probe only returns a hit when
            // called with exactly that mood predicate.
            var vibeMedia = MakeRef("vibe1");
            var store = new FakeRequestStore();
            store.AddPending(Now.AddMinutes(10), moods: ["dreamy"]);
            var probe = new FakeRequestCatalogProbe
            {
                OnFindVibe = (moods, _) => moods.Contains("dreamy") ? vibeMedia : null,
            };
            var provider = BuildProvider(store, probe, overrideEnvelope: true);

            // Act.
            var result = await provider.TryFulfillAsync(Envelope, CancellationToken.None);

            // Assert: the mood-matched candidate came back — the vibe machinery, not a T89 match, won.
            Assert.Equal(vibeMedia.MediaId, result?.Candidate.Media.MediaId);
        }
    }

    // -------------------------------------------------------------------------
    // SAD PATH
    // -------------------------------------------------------------------------

    public static class ScenarioOverrideFalse
    {
        [Fact]
        public static async Task OverrideFalseIdlesAnOffEnvelopeMatchToExpiry()
        {
            // Arrange: OverrideEnvelope=false, and the probe always rejects (simulating a candidate
            // outside the active envelope).
            var store = new FakeRequestStore();
            var id = store.AddPending(Now.AddMinutes(10), matchedMediaId: 1);
            var probe = new FakeRequestCatalogProbe { OnGetSelectableById = (_, _) => null };
            var provider = BuildProvider(store, probe, overrideEnvelope: false);

            // Act.
            await provider.TryFulfillAsync(Envelope, CancellationToken.None);

            // Assert: the row is untouched — still pending, left to idle toward its own expiry rather
            // than being consumed or flipped by this failed attempt.
            Assert.Equal("pending", store.StatusOf(id));
        }
    }

    public static class ScenarioVetoIsLaw
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public static async Task ANeverPlayFlipAfterMatchingStopsFulfillmentInEitherMode(bool overrideEnvelope)
        {
            // Arrange: the probe always vetoes (simulating an operator never-play flip after the T89
            // match) — regardless of which envelope argument it receives in either mode.
            var store = new FakeRequestStore();
            store.AddPending(Now.AddMinutes(10), matchedMediaId: 1);
            var probe = new FakeRequestCatalogProbe { OnGetSelectableById = (_, _) => null };
            var provider = BuildProvider(store, probe, overrideEnvelope);

            // Act.
            var result = await provider.TryFulfillAsync(Envelope, CancellationToken.None);

            // Assert: the veto holds regardless of mode — never bypassed.
            Assert.Null(result);
        }
    }

    public static class ScenarioExpiry
    {
        [Fact]
        public static async Task ARequestPastItsWindowIsMarkedExpiredAndIgnored()
        {
            // Arrange: a pending request whose window already elapsed.
            var store = new FakeRequestStore();
            var id = store.AddPending(Now.AddMinutes(-1), matchedMediaId: 1);
            var probe = new FakeRequestCatalogProbe { OnGetSelectableById = (_, _) => MakeRef("t1") };
            var provider = BuildProvider(store, probe, overrideEnvelope: true);

            // Act: the opportunistic sweep runs at the top of this attempt.
            await provider.TryFulfillAsync(Envelope, CancellationToken.None);

            // Assert: the row is marked expired, not fulfilled — the sweep caught it before selection.
            Assert.Equal("expired", store.StatusOf(id));
        }
    }
}
