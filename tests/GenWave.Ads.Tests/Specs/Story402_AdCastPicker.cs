// STORY-402 — Every generated spot rides two distinct voices from the cast pool (SPEC F167 · closes gh-#702 · PLAN T415)
//
// AC1's "settings live on the allowlist" facts moved to tests/GenWave.Host.Tests/Specs/Story402_AdCastPicker.cs
// (PLAN T415 review R9) — StationSettingsAllowlist is a GenWave.Host-owned type, and this project never
// references GenWave.Host.
//
// Split deliberately: AdCastPicker.Pick is a PURE function (no I/O, no logger — see that class's own
// remarks), so every fact about ITS rule set (AC2, AC4, AC5, AC7's plan shape) calls it directly — fast,
// exact, and immune to anything the worker's render plumbing might otherwise hide a broken rule behind.
// The facts that are genuinely about WIRING — persistence (AC2), the render actually reaching Kokoro with
// the cast the plan named (AC3), an owner draft's own plan surviving untouched (AC6), and the fallback's
// own INFO line + render success (AC7) — drive a real AdSpotWorker tick through AdSpotWorkerHarness.

namespace GenWave.Ads.Tests.Specs;

using System.Text.Json;
using GenWave.Ads.Tests.Fakes;
using GenWave.Ads.Tests.Support;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging;

public static class FeatureAdCastPickerBuildsAVoicePlan
{
    static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    const string StationVoice = AdSpotWorkerHarness.StationVoice;

    static AdSpot Spot(long id = 1, string brand = "Acme", string? packSlug = null, AdSource source = AdSource.Llm) =>
        new(
            id, SponsorId: 1, SponsorName: brand, $"{brand} spot", Brief: null, Script: null, source, packSlug,
            SpotSeconds: 30, VoicePlan: null, BedMediaId: null, AdState.Rendering, FailReason: null, MediaId: null,
            Generation: 1, CreatedAt: DateTime.UtcNow, StateChangedAt: DateTime.UtcNow, RenderedAt: null,
            RetiredAt: null, Version: "1");

    // PLAN T416 review F3+O3: AdLiveSettings.BedFadeMs no longer defaults (that record's own remarks
    // — AdLiveSettingsReader is the ONE construction site with a real default) — this helper is not
    // that reader, so it names the reader's own DefaultBedFadeMs constant explicitly rather than
    // re-inventing 300 as a second magic number nobody but this file would ever see.
    static AdLiveSettings Settings(string announcerVoice, params string[] castVoices) =>
        new(
            announcerVoice, castVoices, BedFadeMs: AdLiveSettingsReader.DefaultBedFadeMs,
            BedDuckDb: AdLiveSettingsReader.DefaultBedDuckDb, TargetLufs: AdLiveSettingsReader.DefaultTargetLufs);

    static string EntryFor(AdCastPick pick, string tag) => pick.Entries.Single(e => e.Tag == tag).VoiceId;

    static Dictionary<string, string?> StationSettings(string castVoices, string announcerVoice = "") => new()
    {
        ["Station:Ads:CastVoices"] = castVoices,
        ["Station:Ads:AnnouncerVoice"] = announcerVoice,
    };

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioWorkerBuildsAVoicePlan
    {
        [Fact]
        public void AnnouncerTagMapsToAdsAnnouncerVoice()
        {
            // Given an explicit Station:Ads:AnnouncerVoice, absent from the cast pool itself...
            var settings = Settings("am_fenrir", "af_nova", "am_michael");

            // When a spot is cast...
            var pick = AdCastPicker.Pick(Spot(), settings, StationVoice);

            // Then the ANNOUNCER tag reads the configured announcer voice — not the station voice.
            Assert.Equal("am_fenrir", EntryFor(pick, AdCastPicker.AnnouncerTag));
        }

        [Fact]
        public void Voice1IsPickedDeterministicallyFromCastVoicesMinusAnnouncer()
        {
            // Given the same spot and the same live settings, picked twice...
            var settings = Settings("am_fenrir", "af_nova", "am_michael", "bf_alice");
            var spot = Spot(id: 42, brand: "Bramble & Fitch");

            var first = AdCastPicker.Pick(spot, settings, StationVoice);
            var second = AdCastPicker.Pick(spot, settings, StationVoice);

            // Then VOICE1 comes from the pool (never the announcer) and is the SAME voice both times.
            var voice1 = EntryFor(first, AdCastPicker.Voice1Tag);
            Assert.Contains(voice1, new[] { "af_nova", "am_michael", "bf_alice" });
            Assert.Equal(voice1, EntryFor(second, AdCastPicker.Voice1Tag));
        }

        [Fact]
        public void Voice2IsAlsoDeterministicallyPickedAndDistinctFromVoice1()
        {
            // Given the same spot and the same live settings, picked twice...
            var settings = Settings("am_fenrir", "af_nova", "am_michael", "bf_alice");
            var spot = Spot(id: 42, brand: "Bramble & Fitch");

            var first = AdCastPicker.Pick(spot, settings, StationVoice);
            var second = AdCastPicker.Pick(spot, settings, StationVoice);

            // Then VOICE2 is a DIFFERENT voice from VOICE1, and repeats identically on the retry.
            var voice1 = EntryFor(first, AdCastPicker.Voice1Tag);
            var voice2 = EntryFor(first, AdCastPicker.Voice2Tag);
            Assert.NotEqual(voice1, voice2);
            Assert.Equal(voice2, EntryFor(second, AdCastPicker.Voice2Tag));
        }

        [Fact]
        public async Task VoicePlanIsPersistedOnTheAdSpotRow()
        {
            // Given an approved spot and a live cast pool...
            var harness = AdSpotWorkerHarness.Build(Now, StationSettings("af_nova,am_michael,bf_alice"));
            harness.Store.AddSpot(1, AdState.Approved);

            // When the worker ticks and renders it...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the row's own voice_plan carries all three tags, each with a real voice id.
            var spot = harness.Store.Spots.Single();
            Assert.NotNull(spot.VoicePlan);
            var entries = JsonSerializer.Deserialize<List<AdVoicePlanEntry>>(spot.VoicePlan, AdVoicePlanJson.Options)!;
            Assert.Equal(3, entries.Count);
            Assert.Contains(entries, e => e.Tag == AdCastPicker.AnnouncerTag);
            Assert.Contains(entries, e => e.Tag == AdCastPicker.Voice1Tag);
            Assert.Contains(entries, e => e.Tag == AdCastPicker.Voice2Tag);

            // And the RAW text — independent of AdVoicePlanJson.Options, which a mutation could weaken
            // right alongside a round-trip check that shares the same (now also weakened) options — uses
            // the camelCase key VoicePackRepository's own uninstall-guard SQL joins on (PLAN T415 review
            // R7): "voiceId", never "VoiceId".
            Assert.Contains("\"voiceId\"", spot.VoicePlan, StringComparison.Ordinal);
            Assert.Contains("\"tag\":\"ANNOUNCER\"", spot.VoicePlan, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TheLiveAnnouncerVoiceSettingReachesTheStampedPlanThroughARealTick()
        {
            // Given Station:Ads:AnnouncerVoice set on the SAME live configuration the worker's own
            // AdLiveSettingsReader reads at the top of every tick — every other fact in this class
            // hand-builds AdLiveSettings directly, which never proves the config key itself is wired
            // (PLAN T415 review F1)...
            var harness = AdSpotWorkerHarness.Build(
                Now, StationSettings("af_nova,am_michael,bf_alice", announcerVoice: "am_fenrir"));
            harness.Store.AddSpot(1, AdState.Approved);

            // When the worker ticks and renders it...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the stamped plan's ANNOUNCER entry is the configured voice, and neither reader
            // role picked it up.
            var spot = harness.Store.Spots.Single();
            Assert.NotNull(spot.VoicePlan);
            var entries = JsonSerializer.Deserialize<List<AdVoicePlanEntry>>(spot.VoicePlan, AdVoicePlanJson.Options)!;
            Assert.Equal("am_fenrir", entries.Single(e => e.Tag == AdCastPicker.AnnouncerTag).VoiceId);
            Assert.NotEqual("am_fenrir", entries.Single(e => e.Tag == AdCastPicker.Voice1Tag).VoiceId);
            Assert.NotEqual("am_fenrir", entries.Single(e => e.Tag == AdCastPicker.Voice2Tag).VoiceId);
        }
    }

    public sealed class ScenarioRenderUsesDistinctVoices
    {
        [Fact]
        public async Task KokoroReceivesAtLeastTwoRequestsWithDistinctVoiceIds()
        {
            // Given a spot whose SCRIPT actually uses all three cast tags (AdRenderService.ResolveCast
            // sources its cast list from the script's own line tags, never the voice_plan directly —
            // a tag the script never speaks never reaches Kokoro regardless of what the plan names it),
            // and a live cast pool wide enough to cast two distinct non-announcer voices...
            var harness = AdSpotWorkerHarness.Build(Now, StationSettings("af_nova,am_michael,bf_alice"));
            harness.Store.AddSpot(
                1, AdState.Approved,
                script: "ANNOUNCER: Come on down.\nVOICE1: Prices you won't believe.\nVOICE2: Hurry in today.");

            // When the worker ticks and renders it...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the cast Kokoro actually received carries at least two distinct voice ids.
            var cast = harness.Author.LastRequest!.Cast;
            var distinctVoiceIds = cast.Select(member => member.Voice.VoiceId).Distinct(StringComparer.Ordinal).Count();
            Assert.True(
                distinctVoiceIds >= 2,
                $"Expected at least two distinct voice ids, got: {string.Join(", ", cast.Select(m => $"{m.Tag}={m.Voice.VoiceId}"))}");
        }
    }

    public sealed class ScenarioRegenerationReCastsIdentically
    {
        [Fact]
        public void ARetryReproducesAByteIdenticalVoicePlan()
        {
            // Given the SAME spot and live settings, picked twice (the "regeneration" a retry means)...
            var settings = Settings("am_fenrir", "af_nova", "am_michael", "bf_alice");
            var spot = Spot(id: 7, brand: "Bramble & Fitch", packSlug: "hardware-classics");

            var first = AdCastPicker.Pick(spot, settings, StationVoice);
            var second = AdCastPicker.Pick(spot, settings, StationVoice);

            // Then the serialized plan is byte-identical, not merely equivalent.
            Assert.Equal(AdVoicePlanJson.Serialize(first.Entries), AdVoicePlanJson.Serialize(second.Entries));
        }

        [Fact]
        public void TheSeedCanonicalStringJoinsIdSourceTokenAndBrand() =>
            // Pins the exact seed input (PLAN T415 review R3) — the ONE place "what feeds the RNG" can
            // silently narrow (e.g. dropping brand) without any other fact here noticing, since none of
            // them vary brand while comparing two picks.
            Assert.Equal("42\nowner\nAcme", AdDeterministicSeed.Canonical("42", "owner", "Acme"));
    }

    public sealed class ScenarioAnnouncerExcludedFromCast
    {
        [Fact]
        public void Voice1IsNeverEqualToTheAnnouncerVoice()
        {
            // Given a cast pool where the configured announcer voice is ALSO listed among CastVoices...
            var settings = Settings("am_fenrir", "am_fenrir", "af_nova", "am_michael");

            // When many different spots are cast (many different RNG seeds)...
            for (var id = 1; id <= 30; id++)
            {
                var pick = AdCastPicker.Pick(Spot(id), settings, StationVoice);

                // Then VOICE1 never reads as the announcer.
                Assert.NotEqual("am_fenrir", EntryFor(pick, AdCastPicker.Voice1Tag));
            }
        }

        [Fact]
        public void Voice2IsNeverEqualToTheAnnouncerVoice()
        {
            var settings = Settings("am_fenrir", "am_fenrir", "af_nova", "am_michael");

            for (var id = 1; id <= 30; id++)
            {
                var pick = AdCastPicker.Pick(Spot(id), settings, StationVoice);
                Assert.NotEqual("am_fenrir", EntryFor(pick, AdCastPicker.Voice2Tag));
            }
        }
    }

    public sealed class ScenarioOwnerDraftsKeepTheirExplicitVoicePlan
    {
        [Fact]
        public async Task TheWorkerDoesNotOverwriteAnExplicitVoicePlanOnAnOwnerDraft()
        {
            // Given an owner draft, already approved, that already carries its own explicit voice plan...
            const string explicitPlan =
                """[{"tag":"ANNOUNCER","voiceId":"am_onyx","pace":1},{"tag":"VOICE1","voiceId":"am_onyx","pace":1},{"tag":"VOICE2","voiceId":"am_onyx","pace":1}]""";
            var harness = AdSpotWorkerHarness.Build(Now, StationSettings("af_nova,am_michael,bf_alice"));
            harness.Store.AddExisting(new AdSpot(
                1, SponsorId: 1, SponsorName: "Acme", "Owner spot", Brief: null,
                Script: "ANNOUNCER: Hi there.\nVOICE1: Come on by.",
                AdSource.Owner, PackSlug: null, SpotSeconds: 30, VoicePlan: explicitPlan, BedMediaId: null,
                AdState.Approved, FailReason: null, MediaId: null, Generation: 1, CreatedAt: DateTime.UtcNow,
                StateChangedAt: DateTime.UtcNow, RenderedAt: null, RetiredAt: null, Version: "1"));

            // When the worker ticks and renders it...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the plan is untouched, byte for byte, and a stamp was never even attempted.
            var spot = harness.Store.Spots.Single();
            Assert.Equal(explicitPlan, spot.VoicePlan);
            Assert.Equal(0, harness.Store.StampVoicePlanCallCount);
        }
    }

    /// <summary>The SAME never-re-pick posture
    /// <see cref="ScenarioOwnerDraftsKeepTheirExplicitVoicePlan"/> already proves for an owner's own
    /// explicit plan, driven instead through the shape a STORY-424 preview render actually leaves
    /// behind: a Draft/Approved row that already carries <see cref="AdSpot.VoicePlan"/> from an earlier
    /// preview, not an owner's own choice. <see cref="AdSpotStamper.StampCastIfNeededAsync"/>'s own
    /// <c>if (spot.VoicePlan is not null) return spot;</c> guard does not distinguish the two — this
    /// fact exists so the SQL guard's own draft/approved/rendering widening (PLAN T442) stays proven at
    /// THIS layer too, not merely in the SQL specs (Story402_AdSpotStampVoicePlanSql.cs).</summary>
    public sealed class ScenarioADraftAlreadyStampedByAPreviewIsNotRePicked
    {
        [Fact]
        public async Task TheWorkerDoesNotRestampAPreviewsVoicePlan()
        {
            // Given an LLM-generated spot, approved but never yet claimed into Rendering, that already
            // carries a voice plan (exactly what AdSpotStamper.StampCastIfNeededAsync leaves on a row
            // once a STORY-424 preview has run against it)...
            const string previewStampedPlan =
                """[{"tag":"ANNOUNCER","voiceId":"am_onyx","pace":1},{"tag":"VOICE1","voiceId":"am_onyx","pace":1},{"tag":"VOICE2","voiceId":"am_onyx","pace":1}]""";
            var harness = AdSpotWorkerHarness.Build(Now, StationSettings("af_nova,am_michael,bf_alice"));
            harness.Store.AddExisting(new AdSpot(
                1, SponsorId: 1, SponsorName: "Acme", "Acme spot", Brief: null,
                Script: "ANNOUNCER: Hi there.\nVOICE1: Come on by.",
                AdSource.Llm, PackSlug: null, SpotSeconds: 30, VoicePlan: previewStampedPlan, BedMediaId: null,
                AdState.Approved, FailReason: null, MediaId: null, Generation: 1, CreatedAt: DateTime.UtcNow,
                StateChangedAt: DateTime.UtcNow, RenderedAt: null, RetiredAt: null, Version: "1"));

            // When the worker claims it into Rendering and renders it...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the preview's own plan is untouched, byte for byte, and the store's own stamp call
            // was never even attempted — AdSpotStamper's C# guard short-circuits before it ever reaches
            // IAdSpotStore.StampVoicePlanIfNullAsync.
            var spot = harness.Store.Spots.Single();
            Assert.Equal(previewStampedPlan, spot.VoicePlan);
            Assert.Equal(0, harness.Store.StampVoicePlanCallCount);
        }
    }

    // ---------------------------------------------------------------------
    // DEGRADED PATH — the pool still casts something, just not two distinct readers
    // ---------------------------------------------------------------------

    public sealed class ScenarioThinPoolDegradesToOneVoice
    {
        [Fact]
        public void WithExactlyOneNonAnnouncerCandidateVoice1AndVoice2ShareIt()
        {
            // Given a cast pool that narrows to exactly one candidate once the effective announcer
            // is stripped out of it (SPEC F167.2, PLAN T415 review R4)...
            var settings = Settings("am_fenrir", "af_nova");

            // When a spot is cast...
            var pick = AdCastPicker.Pick(Spot(), settings, StationVoice);

            // Then the outcome is ThinPool, the announcer still reads its own line, and VOICE1/VOICE2
            // both share the one remaining candidate — never the station voice, never each other's
            // absence.
            Assert.Equal(AdCastOutcome.ThinPool, pick.Outcome);
            Assert.Equal("am_fenrir", EntryFor(pick, AdCastPicker.AnnouncerTag));
            Assert.Equal("af_nova", EntryFor(pick, AdCastPicker.Voice1Tag));
            Assert.Equal("af_nova", EntryFor(pick, AdCastPicker.Voice2Tag));
        }

        [Fact]
        public async Task AnInfoLogLinePerTickNamesTheThinPool()
        {
            // Given an approved spot and a live cast pool that narrows to exactly one candidate once
            // the configured announcer voice is stripped out of it...
            var logger = new CapturingLogger<AdSpotStamper>();
            var harness = AdSpotWorkerHarness.Build(
                Now, StationSettings("af_nova,am_fenrir", announcerVoice: "am_fenrir"), stamperLogger: logger);
            harness.Store.AddSpot(1, AdState.Approved);

            // When the worker ticks...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then exactly one INFO line names the thin pool.
            var infoLines = logger.Entries
                .Where(e => e.Level == LogLevel.Information
                    && e.Message.Contains("same voice reads every part", StringComparison.Ordinal))
                .ToList();
            Assert.Single(infoLines);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioEmptyCastFallsBackHonestly
    {
        [Fact]
        public void EveryTagMapsToTheStationVoiceWhenCastVoicesIsEmpty()
        {
            // Given an EMPTY cast pool, even with an announcer voice configured...
            var settings = Settings("am_fenrir");

            // When a spot is cast...
            var pick = AdCastPicker.Pick(Spot(), settings, StationVoice);

            // Then every tag — including the announcer — falls back to the station voice (F167.4,
            // taken literally: the configured announcer voice is NOT used here).
            Assert.Equal(AdCastOutcome.EmptyPool, pick.Outcome);
            Assert.All(pick.Entries, entry => Assert.Equal(StationVoice, entry.VoiceId));
        }

        [Fact]
        public async Task AnInfoLogLinePerTickNamesTheEmptyPool()
        {
            // Given an approved spot and no live cast pool at all...
            var logger = new CapturingLogger<AdSpotStamper>();
            var harness = AdSpotWorkerHarness.Build(Now, StationSettings(castVoices: ""), stamperLogger: logger);
            harness.Store.AddSpot(1, AdState.Approved);

            // When the worker ticks...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then exactly one INFO line names the fallback — one render per tick, one pick per render,
            // so one line per tick follows by construction (no separate rate-limit state needed).
            var infoLines = logger.Entries
                .Where(e => e.Level == LogLevel.Information && e.Message.Contains("station voice", StringComparison.Ordinal))
                .ToList();
            Assert.Single(infoLines);
        }

        [Fact]
        public async Task TheRenderStillSucceedsInTheFallback()
        {
            // Given an approved spot and no live cast pool at all...
            var harness = AdSpotWorkerHarness.Build(Now, StationSettings(castVoices: ""));
            harness.Store.AddSpot(1, AdState.Approved);

            // When the worker ticks...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the render still completes — the fallback plan is still a fully valid one.
            var spot = harness.Store.Spots.Single();
            Assert.Equal(AdState.Ready, spot.State);
            Assert.Equal(1, harness.Store.MarkReadyCallCount);
        }
    }
}
