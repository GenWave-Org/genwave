// STORY-336 — The face on the stream (SPEC F129.4/.5/.6, gh-#297 · PLAN T300)
//
// BDD specification — xUnit. ArtworkUrlResolver's per-kind mapping (amends F88.4): drives
// ArtworkUrlResolver.ResolveAsync directly against FakeActivePersonaAccessor/FakePersonaAvatarStore
// doubles (Story223's own ArtworkUrlResolver-direct idiom) — no WebApplicationFactory needed, this
// is a pure push-path resolver fact, not an endpoint one.
//
// F129.6's live ICY half (a metadata-aware client observing url= change mid-stream) is the T301
// wire's own acceptance, not a unit fact — this suite pins the annotation VALUE the resolver
// produces, which is everything T301's wire needs to already be correct.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Artwork;
using GenWave.Host.Engine;
using GenWave.Host.Options;
using GenWave.Host.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheFaceOnTheStream
{
    const string PublicBaseUrl = "https://example.test";
    const long PersonaId = 1;
    const string PersonaName = "Nova";
    const string FaceToken = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";

    static readonly GenWave.Core.Domain.Loudness DefaultLoudness = new(-16.0, -1.0, Measurable: true);
    static readonly byte[] FaceBytes = [0xDE, 0xAD, 0xBE, 0xEF];

    /// <summary>An <see cref="FakeActivePersonaAccessor"/> scripted so
    /// <see cref="ArtworkUrlResolver.ResolveDjTokenAsync"/>'s identity-agreement check succeeds for
    /// <see cref="PersonaId"/>/<see cref="PersonaName"/> — mirrors
    /// <c>Story335_TheFaceOnThePublicSurface.cs</c>'s own "seed both to the SAME value" idiom for
    /// <c>SpectatorController.DjIdentityAgrees</c>.</summary>
    static FakeActivePersonaAccessor AgreeingAccessor()
    {
        var accessor = new FakeActivePersonaAccessor { ActivePersonaId = PersonaId };
        accessor.Names[PersonaId] = PersonaName;
        return accessor;
    }

    static async Task<FakePersonaAvatarStore> FacedAvatarStoreAsync()
    {
        var store = new FakePersonaAvatarStore();
        await store.UpsertAsync(
            new PersonaAvatarInput(PersonaId, FaceBytes, "sha256-stub", FaceToken, PersonaAvatarSource.Upload, null),
            CancellationToken.None);
        return store;
    }

    static ArtworkUrlResolver Resolver(
        FakeActivePersonaAccessor accessor, FakePersonaAvatarStore avatarStore,
        TimeProvider? timeProvider = null, string publicBaseUrl = PublicBaseUrl,
        IStationImageStore? stationImageStore = null) => new(
        new FakeOptionsMonitor<StationOptions>(new StationOptions { PublicBaseUrl = publicBaseUrl }),
        new FakeArtworkTokenStore(), accessor,
        new PersonaAvatarTokenCache(
            avatarStore, timeProvider ?? TimeProvider.System, NullLogger<PersonaAvatarTokenCache>.Instance),
        new StationImageCache(
            stationImageStore ?? new FakeStationImageStore(), timeProvider ?? TimeProvider.System,
            NullLogger<StationImageCache>.Instance));

    // ---------------------------------------------------------------------
    // HAPPY PATH — the mapping
    // ---------------------------------------------------------------------

    public sealed class ScenarioSingleVoiceSpeechWearsItsFace
    {
        [Fact]
        public async Task APersonaAttributedItemWithAWornFaceStampsTheDjTokenUrl()
        {
            // LeadIn/BackAnnounce/TimeDate/ceremony kinds, persona wears a face
            // → annotation url= …/spectator/api/artwork/dj/<token>.
            var resolver = Resolver(AgreeingAccessor(), await FacedAvatarStoreAsync());
            var item = new MediaItem("tts:leadin1", "/tts/leadin1.wav", "GenWave", DefaultLoudness,
                DjName: PersonaName, SegmentKind: SegmentKind.LeadIn);

            var url = await resolver.ResolveAsync(item, CancellationToken.None);

            Assert.Equal($"{PublicBaseUrl}/spectator/api/artwork/dj/{FaceToken}", url);
        }

        [Fact]
        public async Task AFacelessPersonasItemStampsTheStationImageUrl()
        {
            // Same agreeing identity, but the persona wears no face at all (empty store) — an
            // honest "no face", never an error, falls to the station image exactly like an
            // unattributed item does.
            var resolver = Resolver(AgreeingAccessor(), new FakePersonaAvatarStore());
            var item = new MediaItem("tts:backannounce1", "/tts/backannounce1.wav", "GenWave", DefaultLoudness,
                DjName: PersonaName, SegmentKind: SegmentKind.BackAnnounce);

            var url = await resolver.ResolveAsync(item, CancellationToken.None);

            Assert.Equal($"{PublicBaseUrl}/spectator/api/artwork/station", url);
        }
    }

    public sealed class ScenarioTheStationSpeaksAsTheStation
    {
        [Fact]
        public async Task ACrosstalkItemStampsTheStationImageUrl()
        {
            // Ruled: two voices = the station, never one DJ's face — even though the unit's own
            // persona genuinely wears one (FacedAvatarStoreAsync), Crosstalk never reaches the
            // face-lookup branch at all.
            var resolver = Resolver(AgreeingAccessor(), await FacedAvatarStoreAsync());
            var item = new MediaItem("tts:crosstalk1", "/tts/crosstalk1.wav", "GenWave", DefaultLoudness,
                DjName: PersonaName, SegmentKind: SegmentKind.Crosstalk);

            var url = await resolver.ResolveAsync(item, CancellationToken.None);

            Assert.Equal($"{PublicBaseUrl}/spectator/api/artwork/station", url);
        }

        [Fact]
        public async Task IdentsAndSafeItemsStampTheStationImageUrl()
        {
            // A StationId ident always credits the station (gh-#96) even though it carries the
            // unit's own DjName (the Orchestrator's own "unitDjName" stamp) and that persona
            // genuinely wears a face — the kind gate short-circuits before any face lookup.
            var resolver = Resolver(AgreeingAccessor(), await FacedAvatarStoreAsync());
            var item = new MediaItem("tts:ident1", "/tts/ident1.wav", "GenWave", DefaultLoudness,
                DjName: PersonaName, SegmentKind: SegmentKind.StationId);

            var url = await resolver.ResolveAsync(item, CancellationToken.None);

            Assert.Equal($"{PublicBaseUrl}/spectator/api/artwork/station", url);
        }

        [Fact]
        public async Task MusicItemsAreByteIdenticalToPreF129()
        {
            // A real music item (numeric MediaId, no SegmentKind/DjName) resolves through the
            // ORIGINAL per-track token branch, untouched — the accessor/avatar store below are both
            // scripted with a real agreeing face just to prove the music path never even looks at
            // them.
            var resolver = Resolver(AgreeingAccessor(), await FacedAvatarStoreAsync());
            var item = new MediaItem("42", "/media/42.mp3", "Title", DefaultLoudness);

            var url = await resolver.ResolveAsync(item, CancellationToken.None);

            Assert.Equal($"{PublicBaseUrl}/spectator/api/artwork/tok42", url);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — F131.2's own token-versioned station URL (PLAN T307)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheStationImageTokenVersionsWhenCustomized
    {
        const string StationImageToken = "d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1";

        static FakeStationImageStore CustomizedStationImageStore()
        {
            var store = new FakeStationImageStore();
            store.Seed(new StationImage([0xFA, 0xCE], 2, "sha256-stub", StationImageToken, DateTime.UtcNow));
            return store;
        }

        [Fact]
        public async Task ACrosstalkItemStampsTheTokenVersionedStationUrlWhenCustomized()
        {
            // SPEC F131.2: customized → the TOKEN-VERSIONED station URL, not the shipped constant —
            // the SAME Crosstalk kind ScenarioTheStationSpeaksAsTheStation's own
            // ACrosstalkItemStampsTheStationImageUrl fact pins to the constant when NOT customized;
            // this fact proves the OTHER half of F131.2's own "customized vs otherwise" branch.
            var resolver = Resolver(
                AgreeingAccessor(), await FacedAvatarStoreAsync(), stationImageStore: CustomizedStationImageStore());
            var item = new MediaItem("tts:crosstalk2", "/tts/crosstalk2.wav", "GenWave", DefaultLoudness,
                DjName: PersonaName, SegmentKind: SegmentKind.Crosstalk);

            var url = await resolver.ResolveAsync(item, CancellationToken.None);

            Assert.Equal($"{PublicBaseUrl}/spectator/api/artwork/station/{StationImageToken}", url);
        }

        [Fact]
        public async Task AFacelessPersonasItemStampsTheTokenVersionedStationUrlWhenCustomized()
        {
            var resolver = Resolver(
                AgreeingAccessor(), new FakePersonaAvatarStore(), stationImageStore: CustomizedStationImageStore());
            var item = new MediaItem("tts:backannounce2", "/tts/backannounce2.wav", "GenWave", DefaultLoudness,
                DjName: PersonaName, SegmentKind: SegmentKind.BackAnnounce);

            var url = await resolver.ResolveAsync(item, CancellationToken.None);

            Assert.Equal($"{PublicBaseUrl}/spectator/api/artwork/station/{StationImageToken}", url);
        }
    }

    public sealed class ScenarioTheHotPathStaysCold
    {
        [Fact]
        public async Task PersonaTokenResolutionIssuesNoPerTickRead()
        {
            // Pins PersonaAvatarTokenCache's own ≤30s TTL memo (SPEC F129.5, PLAN T300) — the ONE
            // shared cache both ArtworkUrlResolver (this push path) and SpectatorController (the
            // now-playing poll) read, so the stream and the payload can never observe a
            // stale-vs-fresh token differently for the same instant. Proven here via a counting
            // store fake (FakePersonaAvatarStore.GetTokenByPersonaIdCallCount) and FakeTimeProvider:
            // three resolves inside one staleness window cost exactly one store read; advancing past
            // PersonaAvatarTokenCache.StalenessBound triggers exactly one more.
            var avatarStore = await FacedAvatarStoreAsync();
            var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var resolver = Resolver(AgreeingAccessor(), avatarStore, timeProvider);
            var item = new MediaItem("tts:timedate1", "/tts/timedate1.wav", "GenWave", DefaultLoudness,
                DjName: PersonaName, SegmentKind: SegmentKind.TimeDate);

            await resolver.ResolveAsync(item, CancellationToken.None);
            await resolver.ResolveAsync(item, CancellationToken.None);
            await resolver.ResolveAsync(item, CancellationToken.None);

            Assert.Equal(1, avatarStore.GetTokenByPersonaIdCallCount);

            timeProvider.Advance(PersonaAvatarTokenCache.StalenessBound + TimeSpan.FromSeconds(1));
            await resolver.ResolveAsync(item, CancellationToken.None);

            Assert.Equal(2, avatarStore.GetTokenByPersonaIdCallCount);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the gate
    // ---------------------------------------------------------------------

    public sealed class ScenarioNoBaseUrlMeansNoEmission
    {
        [Fact]
        public async Task WithPublicBaseUrlEmptyNoUrlIsEmittedAtAll()
        {
            // F88.4's gating unchanged — HTTP-only deployments stay honest, even for an item that
            // would otherwise wear a face.
            var resolver = Resolver(AgreeingAccessor(), await FacedAvatarStoreAsync(), publicBaseUrl: string.Empty);
            var item = new MediaItem("tts:leadin2", "/tts/leadin2.wav", "GenWave", DefaultLoudness,
                DjName: PersonaName, SegmentKind: SegmentKind.LeadIn);

            var url = await resolver.ResolveAsync(item, CancellationToken.None);

            Assert.Null(url);
        }
    }

    public sealed class ScenarioIdentityDisagreementFallsToTheStationImage
    {
        // THE ONE IDENTITY GATE (SPEC F129.6, PLAN T300 fix round F3/F4) — DjIdentity.Agrees,
        // shared with SpectatorController's own djAvatarUrl gate: the payload and the stream must
        // never disagree on which face belongs to the on-air voice, so "can't verify" degrades
        // exactly like "verified disagreement" — never a free pass to the wrong face.

        [Fact]
        public async Task ADisagreeingCachedNameStampsTheStationImageUrl()
        {
            // The on-air persona genuinely wears a face, but the item's OWN DjName names someone
            // else — a boundary-skew race between the item-truth attribution and the resolver's
            // own on-air answer (ArtworkUrlResolver's own RIGHT FACE OR NO FACE remarks).
            var accessor = new FakeActivePersonaAccessor { ActivePersonaId = PersonaId };
            accessor.Names[PersonaId] = "Someone Else";
            var resolver = Resolver(accessor, await FacedAvatarStoreAsync());
            var item = new MediaItem("tts:leadin4", "/tts/leadin4.wav", "GenWave", DefaultLoudness,
                DjName: PersonaName, SegmentKind: SegmentKind.LeadIn);

            var url = await resolver.ResolveAsync(item, CancellationToken.None);

            Assert.Equal($"{PublicBaseUrl}/spectator/api/artwork/station", url);
        }

        [Fact]
        public async Task AnUnverifiableIdentityWithNoCachedNameStampsTheStationImageUrl()
        {
            // No cached name at all for the candidate id (never yet resolved through the ordinary
            // orchestration path, e.g. the process-boot window) — "can't verify" is disagreement,
            // never a free pass.
            var accessor = new FakeActivePersonaAccessor { ActivePersonaId = PersonaId }; // Names left empty
            var resolver = Resolver(accessor, await FacedAvatarStoreAsync());
            var item = new MediaItem("tts:leadin5", "/tts/leadin5.wav", "GenWave", DefaultLoudness,
                DjName: PersonaName, SegmentKind: SegmentKind.LeadIn);

            var url = await resolver.ResolveAsync(item, CancellationToken.None);

            Assert.Equal($"{PublicBaseUrl}/spectator/api/artwork/station", url);
        }

        [Fact]
        public async Task ANullActivePersonaIdStampsTheStationImageUrl()
        {
            // No on-air persona at all (a schedule grid gap) — no candidate id even exists to
            // attempt verifying.
            var accessor = new FakeActivePersonaAccessor(); // ActivePersonaId defaults to null
            var resolver = Resolver(accessor, await FacedAvatarStoreAsync());
            var item = new MediaItem("tts:leadin6", "/tts/leadin6.wav", "GenWave", DefaultLoudness,
                DjName: PersonaName, SegmentKind: SegmentKind.LeadIn);

            var url = await resolver.ResolveAsync(item, CancellationToken.None);

            Assert.Equal($"{PublicBaseUrl}/spectator/api/artwork/station", url);
        }
    }

    public sealed class ScenarioTheStoreNeverThrowsIntoTheResolver
    {
        [Fact]
        public async Task AFaultingStoreDegradesToTheStationImageThenRecoversOnceItRecovers()
        {
            // The never-throws contract + recovery (PLAN T300 fix round F3, mirroring the
            // reviewer's own harness scenario): a faulting store degrades ResolveAsync to the
            // station image, never a thrown exception into the push path — and because a fault
            // memoizes only a permanently-stale sentinel that can never evaluate as fresh (SPEC
            // F129.5's own contract), the very next call still retries the store immediately, with
            // no StalenessBound wait required, and the face returns.
            var avatarStore = await FacedAvatarStoreAsync();
            avatarStore.ThrowOnCallNumber = 1; // only the first store call faults
            var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var resolver = Resolver(AgreeingAccessor(), avatarStore, timeProvider);
            var item = new MediaItem("tts:timedate2", "/tts/timedate2.wav", "GenWave", DefaultLoudness,
                DjName: PersonaName, SegmentKind: SegmentKind.TimeDate);

            var whileFaulting = await resolver.ResolveAsync(item, CancellationToken.None);
            Assert.Equal($"{PublicBaseUrl}/spectator/api/artwork/station", whileFaulting);

            var recovered = await resolver.ResolveAsync(item, CancellationToken.None);
            Assert.Equal($"{PublicBaseUrl}/spectator/api/artwork/dj/{FaceToken}", recovered);

            // Stays memoized like any ordinary successful fetch across a further tick inside the
            // same staleness window — the recovered answer is not a one-shot fluke.
            timeProvider.Advance(PersonaAvatarTokenCache.StalenessBound - TimeSpan.FromSeconds(1));
            var stillGood = await resolver.ResolveAsync(item, CancellationToken.None);
            Assert.Equal($"{PublicBaseUrl}/spectator/api/artwork/dj/{FaceToken}", stillGood);
            Assert.Equal(2, avatarStore.GetTokenByPersonaIdCallCount);
        }
    }

    public sealed class ScenarioACancelledCallersColdFetchNeverPoisonsTheSharedMemo
    {
        [Fact]
        public async Task ACancelledCallerStillLeavesLaterResolvesAnswering()
        {
            // THE F1 PIN (PLAN T300 fix round). Before the fix, PersonaAvatarTokenCache.FetchAsync
            // ran the store read on the FIRST caller's OWN CancellationToken, with no
            // belt-and-braces eviction of a memoized-but-cancelled entry: a spectator request
            // aborting mid-cold-fetch left a permanently CANCELLED Task memoized under that persona
            // id — every LATER resolve (a different caller, or the same persona's very next feeder
            // push) re-awaited that same poisoned entry and threw immediately, wedging the whole
            // broadcast until a restart (the reviewer's own harness: 3 poisoned ticks, the store
            // never re-read).
            //
            // This fact does NOT reproduce that exact poisoning shape — it exercises the SHIPPED
            // cache, which holds via two INDEPENDENT guards: FetchAsync's own
            // CancellationToken.None binding (the store call is never bound to any one caller's
            // token — F1) and GetTokenAsync's own IsCompletedSuccessfully eviction belt-and-braces
            // (F6). Either guard alone keeps THIS fact green; reproducing the original poisoning
            // needs BOTH reverted at once (see the fix round's own red-proof: temporarily
            // re-applying the pre-fix ct-bound-fetch + unconditional-await shape and confirming this
            // exact fact goes RED — with FakePersonaAvatarStore's own Gate honoring the caller's ct
            // via WaitAsync, not just answering on its own schedule). What this fact actually pins:
            // the FIRST, cancelled caller still observes its OWN cancellation (WaitAsync(ct) is
            // per-caller), but the shared fetch keeps running behind it — gated here so the test
            // controls exactly when the store "answers" — and a LATER, uncancelled resolve still
            // gets a good answer once it does.
            var avatarStore = await FacedAvatarStoreAsync();
            avatarStore.Gate = new TaskCompletionSource<string?>();
            var resolver = Resolver(AgreeingAccessor(), avatarStore);
            var item = new MediaItem("tts:leadin7", "/tts/leadin7.wav", "GenWave", DefaultLoudness,
                DjName: PersonaName, SegmentKind: SegmentKind.LeadIn);

            using var cts = new CancellationTokenSource();
            var poisonedCall = resolver.ResolveAsync(item, cts.Token);
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => poisonedCall);

            // The store "recovers" (or simply finishes) — releases whichever fetch(es) are gated,
            // proving the shared memo was never wedged by the first caller's own cancellation.
            avatarStore.Gate.SetResult(FaceToken);
            var url = await resolver.ResolveAsync(item, CancellationToken.None);

            Assert.Equal($"{PublicBaseUrl}/spectator/api/artwork/dj/{FaceToken}", url);
        }
    }
}
