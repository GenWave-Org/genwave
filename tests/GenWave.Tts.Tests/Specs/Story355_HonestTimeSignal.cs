// STORY-355 — The time signal tells the truth late (SPEC F141 · PLAN T326)
//
// BDD specification — xUnit. gh-#526's field data: ~5 misses/day, every overrun shallow (313-362s
// past a 300s budget) — the break just arrives late. The fix stops the signal lying instead of
// dropping: budget widens to 420s as configuration, and past 90s the per-hour template goes honest
// ("just past") — same station voice, zero LLM, forever-cached by rendered text exactly like the
// on-time line (F110.3's own pattern).
//
// The classification itself (on time / late) is an Orchestrator-level decision (GenWave.Orchestration,
// unreachable from this project — the exact Story302/Story228 split this file's own header note below
// explains) computed once at drain time and stamped onto the SegmentRequest it Kicks
// (SegmentRequest.TimeDateFreshness). What lives here, and is provable with no Orchestrator in the
// loop, is everything downstream of that stamp: PatterTemplateRenderer's own copy choice, and the
// forever-cache mechanics (Story302's own precedent, unchanged).
//
// AC4 — "past the budget it still drops, with the WARN" — is proven upstream of this stamp entirely,
// by Story321_LateTimeCheckDies.cs's own ScenarioATimeDateDeferralDrainingLateIsDropped facts, through
// the REAL SpeechDeferralQueue.TryDequeueDue expiry check (SPEC F124.4/F141.3, unchanged by this
// feature): a deferral drained past budget never reaches this project's own render seam at all — no
// SegmentRequest is ever built for it, so there is no third TimeDateFreshness value here to guard
// against (round-2 review finding F3 removed the domain's own Expired member and TtsSegmentSource's
// now-provably-unreachable belt-and-suspenders branch — the enum models on-time/late, the two states
// that actually reach a SegmentRequest).

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureHonestTimeSignal
{
    static SegmentRequest TimeDateRequest(
        DateTimeOffset localNow, TimeAnnouncementFreshness freshness = TimeAnnouncementFreshness.OnTime) =>
        new(SegmentKind.TimeDate, "af_heart", "GenWave", null, localNow, "test-station")
        {
            TimeDateFreshness = freshness,
        };

    static TtsSegmentSource BuildSource(FakeTtsSynthesizer synth, string cacheRoot, ILogger<TtsSegmentSource>? logger = null) =>
        new(
            new TemplateCopyWriter(new PatterTemplateRenderer()),
            synth,
            new FakeLoudnessAnalyzer(),
            new FakeCueAnalyzer(),
            NoCorrections.Provider(),
            NoCorrections.PersonaCache(),
            NoCorrections.PronunciationProvider(),
            NoCorrections.PersonaPronunciationCache(),
            NoCorrections.PersonaPaceCache(),
            new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
            logger ?? NullLogger<TtsSegmentSource>.Instance);

    public static class ScenarioTheBudgetIsConfiguration
    {
        [Fact]
        public static void The_default_budget_is_420_seconds() =>
            Assert.Equal(
                420,
                new StationImagingSettings(ClockAnchoredIdents: false, TimeAnnouncements: false).TimeAnnouncementBudgetSeconds);
    }

    public sealed class ScenarioOnTimeDrainsAirTheClassicLine : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();

        [Fact]
        public async Task An_OnTime_stamp_renders_the_classic_copy()
        {
            var source = BuildSource(synth, cacheRoot);

            await source.RenderAsync(
                TimeDateRequest(new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero)), CancellationToken.None);

            Assert.Equal("It's two o'clock on GenWave.", synth.LastText);
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
        }
    }

    public sealed class ScenarioLateDrainsGoHonest : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        // FakeTtsSynthesizer writes to {OutputDirectory}/{hash}.wav using the same hash formula
        // TtsSegmentSource itself computes (mirrors Story302's own BuildSynthForCache).
        FakeTtsSynthesizer BuildSynthForCache() => new() { OutputDirectory = cacheRoot };

        [Fact]
        public async Task A_Late_stamp_renders_the_just_past_variant()
        {
            var synth = BuildSynthForCache();
            var source = BuildSource(synth, cacheRoot);

            await source.RenderAsync(
                TimeDateRequest(new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero), TimeAnnouncementFreshness.Late),
                CancellationToken.None);

            Assert.Equal("It's just past two o'clock on GenWave.", synth.LastText);
        }

        [Fact]
        public async Task The_late_variant_caches_forever_by_rendered_text()
        {
            var synth = BuildSynthForCache();
            var source = BuildSource(synth, cacheRoot);

            // Same hour-of-day (14:00), a day apart — the SAME "the hour recurs" shape Story302's own
            // cache-hit fact proves for the classic line, applied here to the late variant.
            var first = TimeDateRequest(
                new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero), TimeAnnouncementFreshness.Late);
            var second = TimeDateRequest(
                new DateTimeOffset(2026, 8, 9, 14, 0, 0, TimeSpan.Zero), TimeAnnouncementFreshness.Late);

            await source.RenderAsync(first, CancellationToken.None);
            synth.ResetCallCount();

            await source.RenderAsync(second, CancellationToken.None);

            Assert.Equal(0, synth.CallCount); // cache hit — no re-synthesis (SPEC F141.2)
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
        }
    }
}
