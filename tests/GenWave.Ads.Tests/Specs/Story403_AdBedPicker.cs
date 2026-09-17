// STORY-403 — Every generated spot has music-under-voice on day one (SPEC F168 · closes gh-#708 · PLAN T416)
//
// Split deliberately, the Story402_AdCastPicker precedent one story over: AdBedPicker.Pick is a PURE
// function (no I/O, no logger — see that class's own remarks), so facts about ITS rule set (the pick
// space, determinism) call it directly. The facts that are genuinely about WIRING — persistence
// (AC1), the owner-override guard (AC6), and the empty-pool fallback's own INFO line + dry render
// (AC5) — drive a real AdSpotWorker tick through AdSpotWorkerHarness. The role-filter guarantee
// itself (AC1's "bed, not sting or station_id") is a SQL-layer fact, not a picker-layer one — see the
// remarks on ThePickedRowsRoleIsBedNotStingOrStationId below for the full chain.

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Fakes;
using GenWave.Ads.Tests.Support;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging;

public static class FeatureAdBedPickerStampsABedPerSpot
{
    static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    static Dictionary<string, string?> StationSettings(string? bedFadeMs = null) => new()
    {
        ["Station:Ads:BedFadeMs"] = bedFadeMs,
    };

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheWorkerPicksABedDeterministically
    {
        [Fact]
        public async Task BedMediaIdIsStampedOnTheAdSpotRow()
        {
            // Given an approved spot and a live bed pool for the ads library the worker resolves,
            // with both candidate bed rows resolvable through the admin media lookup (so whichever one
            // AdBedPicker.Pick chooses can actually reach the render, not just the store write)...
            var harness = AdSpotWorkerHarness.Build(Now);
            harness.Store.AddSpot(1, AdState.Approved);
            harness.BedPool.Seed(harness.AdsLibraryId, 501, 777);
            harness.AdminLookup
                .Add(501, AdSpotWorkerHarness.MakeMediaRow(501, eligible: true), harness.AdsLibraryId)
                .Add(777, AdSpotWorkerHarness.MakeMediaRow(777, eligible: true), harness.AdsLibraryId);

            // When the worker ticks and renders it...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the row's own bed_media_id carries whatever AdBedPicker.Pick computed for this
            // exact spot id against this exact pool — the worker persists the PICKER's own answer,
            // not some value it derived independently.
            var spot = harness.Store.Spots.Single();
            var expectedPick = AdBedPicker.Pick(spot.Id, [501, 777]);
            Assert.Equal(expectedPick, spot.BedMediaId);

            // PLAN T416 review F2: the RENDER itself received the same pick, not the pre-stamp row —
            // mutating StampBedIfNeededAsync's `return stamped ?? spot;` to `return spot;` stays green
            // against the store-only assertion above (the DB write still lands unconditionally,
            // StampBedIfNullAsync is called either way); only the render-side request, built from the
            // RETURNED value, goes stale (Bed stays null) under that mutation.
            Assert.NotNull(harness.Author.LastRequest!.Bed);
            Assert.Equal($"/authored/ads/{expectedPick}.wav", harness.Author.LastRequest.Bed.Path);
        }

        [Fact]
        public void ThePickedRowsRoleIsBedNotStingOrStationId()
        {
            // AdBedPicker.Pick itself has NO notion of "role" — it only ever indexes into whatever
            // pool it is handed (see that class's own remarks: "just a seeded index into an
            // already-filtered, already-ordered pool"). The role guarantee proper — that
            // IAdBedPool.ListReadyBedIdsAsync's own jingle_role = 'bed' predicate excludes sting and
            // station_id rows — is proven where the filtering actually happens: the SQL text pin
            // (AdBedPoolRepository.PoolSql, tests/GenWave.MediaLibrary.Tests/Specs/Story403_AdBedPoolSql.cs)
            // and the real-Postgres fact against a genuinely mixed bed/sting/station_id install
            // (ScenarioBedPoolQueryFindsTheBeds, tests/GenWave.Host.Tests/Specs/Story399_JinglePackInstall.cs).
            //
            // What THIS layer can and must prove instead: the picker never widens its own output space
            // beyond the pool it was handed — a pool that (by IAdBedPool's own contract, proven at the
            // two seams above) can only ever contain bed-role ids never invents a sting/station_id id
            // out of nowhere. Many different spot ids (many different RNG seeds), one pool of two
            // known ids.
            var pool = new long[] { 501, 777 };
            for (long id = 1; id <= 30; id++)
            {
                var pick = AdBedPicker.Pick(id, pool);
                Assert.NotNull(pick);
                Assert.Contains(pick.Value, pool);
            }
        }

        [Fact]
        public void ThePickIsDeterministicInSpotId()
        {
            // Given the same spot id and the same pool, picked twice (M3's own red: a Random.Shared
            // seed would make this flaky, not merely fail once)...
            var pool = new long[] { 501, 777, 999 };

            var first = AdBedPicker.Pick(42, pool);
            var second = AdBedPicker.Pick(42, pool);

            // Then the SAME row comes back both times, and it is drawn from the pool.
            Assert.NotNull(first);
            Assert.Contains(first.Value, pool);
            Assert.Equal(first, second);
        }
    }

    public sealed class ScenarioTheRenderDucksTheBed
    {
        [Fact(Skip = "gate: T422 wire — AC2 (ffmpeg-provable waveform proof against a real rendered spot; T416 already emits the duck volume filter and passes BedDuckDb through unchanged — this fact needs a genuine rendered artifact, T422's own surface, not a filter-string pin)")]
        public void TheRenderedWaveformShowsABedAtTheDuckedLevelUnderTheVoice() { }

        [Fact(Skip = "gate: T422 wire — AC2 (ffmpeg-provable waveform proof; T416 does not change the duck/tail shape at all — F168 only adds a fade ON TOP of the existing tail, this fact needs a genuine rendered artifact, T422's own surface)")]
        public void TheBedRunsUnduckedInTheTailAfterTheVoiceEnds() { }
    }

    public sealed class ScenarioTheTailFadeHonorsAdsBedFadeMs
    {
        [Fact(Skip = "gate: T422 wire — AC3 (ffmpeg silencedetect against a real rendered spot; T416 already proves the afade=t=out filter string is emitted with the configured duration — GenWave.Tts.Tests/Specs/Story391_AdRenderAssembly.cs ScenarioTheTailFadeIsAPureFilterSuffix — this fact needs a genuine rendered artifact, T422's own surface)")]
        public void TheBedFadesToSilenceAcrossTheTrailingAdsBedFadeMsWindow() { }
    }

    /// <summary>
    /// SPEC F168.4; STORY-403; PLAN T416 review R6(a) — proves <c>Station:Ads:BedFadeMs</c>'s own
    /// ms→sec conversion actually reaches the request Kokoro/the mixer receive, through a REAL worker
    /// tick against the SAME live <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
    /// <see cref="AdRenderService"/>'s own per-render read goes through — never a hand-built
    /// <c>CastAssemblyRequest</c> that would only prove the constructor accepts the value, not that
    /// the config key is wired. The sibling half — that the mixer's own filter string actually emits
    /// an <c>afade=t=out</c> for a non-zero <c>BedFadeSeconds</c> — lives in
    /// <c>GenWave.Tts.Tests/Specs/Story391_AdRenderAssembly.cs</c>'s own
    /// <c>ScenarioTheTailFadeIsAPureFilterSuffix</c> (plain text, not a <c>cref</c>: this project
    /// carries no reference to GenWave.Tts.Tests).
    /// </summary>
    public sealed class ScenarioBedFadeMsReachesTheRenderRequest
    {
        [Fact]
        public async Task FiveHundredMillisecondsReachesTheCastAssemblyRequestAsPointFiveSeconds()
        {
            // Given Station:Ads:BedFadeMs=500 on the SAME live configuration AdRenderService's own
            // per-render read goes through...
            var harness = AdSpotWorkerHarness.Build(Now, StationSettings(bedFadeMs: "500"));
            harness.Store.AddSpot(1, AdState.Approved);

            // When the worker ticks and renders it...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the request the (fake) cast segment author actually received carries the
            // MILLISECONDS→SECONDS conversion, not the raw configured value (M5's own red: dropping
            // this wire leaves BedFadeSeconds at its 0.0 default regardless of what is configured).
            Assert.Equal(0.5, harness.Author.LastRequest!.BedFadeSeconds);
        }
    }

    public sealed class ScenarioRegenerationRePicksTheSameBed
    {
        [Fact]
        public void ARetryReproducesTheByteIdenticalBedMediaId()
        {
            // Given the SAME spot id and the SAME installed pool, picked twice (the "regeneration" a
            // retry means — SPEC F168.3, distinct from AC1's own determinism fact: this one proves
            // the SAME guarantee survives a retry specifically, not just two calls in a row)...
            var pool = new long[] { 501, 777, 999 };
            const long spotId = 7;

            var first = AdBedPicker.Pick(spotId, pool);
            var second = AdBedPicker.Pick(spotId, pool);

            // Then the picked id is byte-identical, not merely equivalent.
            Assert.Equal(first, second);
        }
    }

    public sealed class ScenarioOwnerOverrideWins
    {
        [Fact]
        public async Task TheWorkersPickerDoesNotRunWhenBedMediaIdIsSetAtApprovalTime()
        {
            // Given an owner draft, already approved, that already carries its own explicit bed pick...
            var harness = AdSpotWorkerHarness.Build(Now);
            harness.BedPool.Seed(harness.AdsLibraryId, 501, 777);
            harness.Store.AddExisting(new AdSpot(
                1, SponsorId: 1, SponsorName: "Acme", "Owner spot", Brief: null, Script: "ANNOUNCER: Hi there.",
                AdSource.Owner, PackSlug: null, SpotSeconds: 30, VoicePlan: null, BedMediaId: 999,
                AdState.Approved, FailReason: null, MediaId: null, Generation: 1, CreatedAt: DateTime.UtcNow,
                StateChangedAt: DateTime.UtcNow, RenderedAt: null, RetiredAt: null, Version: "1"));

            // When the worker ticks and renders it...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the bed is untouched (never even re-picked to the SAME value by coincidence — the
            // pool never contains 999), no pool query ran, and a stamp was never even attempted (M2's
            // own red: dropping the BedMediaId is null check would make this call the pool anyway).
            var spot = harness.Store.Spots.Single();
            Assert.Equal(999, spot.BedMediaId);
            Assert.Equal(0, harness.BedPool.CallCount);
            Assert.Equal(0, harness.Store.StampBedCallCount);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnEmptyBedPoolFallsBackHonestly
    {
        [Fact]
        public async Task BedMediaIdStaysNullWhenNoJinglePackIsInstalled()
        {
            // Given an approved spot and NO installed background-music pack (the pool the worker
            // resolves for the ads library stays empty by default — FakeAdBedPool's own remarks)...
            var harness = AdSpotWorkerHarness.Build(Now);
            harness.Store.AddSpot(1, AdState.Approved);

            // When the worker ticks and renders it...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the row's own bed_media_id stays null — no pick invented out of nothing.
            var spot = harness.Store.Spots.Single();
            Assert.Null(spot.BedMediaId);
        }

        [Fact]
        public async Task OneInfoLogPerTickNamesTheEmptyPool()
        {
            // Given an approved spot and no installed pack, with a logger that captures every entry...
            var logger = new CapturingLogger<AdSpotStamper>();
            var harness = AdSpotWorkerHarness.Build(Now, stamperLogger: logger);
            harness.Store.AddSpot(1, AdState.Approved);

            // When the worker ticks...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then exactly one INFO line names the fallback — one render per tick, one pick per
            // render, so one line per tick follows by construction (the Story402 AdCastPicker
            // precedent, one seam over). Plain-name-first wording (gh-#707): "background music", never
            // the bare jargon "bed" (M7's own red: logging at Warning instead of Information would
            // make this fact find nothing).
            var infoLines = logger.Entries
                .Where(e => e.Level == LogLevel.Information
                    && e.Message.Contains("No background music is installed", StringComparison.Ordinal))
                .ToList();
            Assert.Single(infoLines);
        }

        [Fact]
        public async Task TheRenderStillProducesADrySpot()
        {
            // Given an approved spot and no installed pack...
            var harness = AdSpotWorkerHarness.Build(Now);
            harness.Store.AddSpot(1, AdState.Approved);

            // When the worker ticks...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the render still completes (F161.2's own "a null bed is a legal render" wire) —
            // an empty pool degrades to an unbedded render, never a failure.
            var spot = harness.Store.Spots.Single();
            Assert.Equal(AdState.Ready, spot.State);
            Assert.Equal(1, harness.Store.MarkReadyCallCount);
        }
    }

    /// <summary>
    /// PLAN T416 review O2 — the bed step sits inside the SAME stuck-rendering safety net every other
    /// render-time step already does. No T415-era fact pins the cast-stamp step against this specific
    /// failure mode either (grepped: none exists) — written fresh here against the bed step this task
    /// actually adds. A pool that throws mid-tick must never wedge the row anywhere but the SAME
    /// rendering state a crashed worker process would leave it in — the tick's own outer catch
    /// swallows the fault (never aborts the loop), and the lifecycle guardian's sweep is what
    /// eventually recovers it, exactly like any other mid-render crash.
    /// </summary>
    public sealed class ScenarioTheBedStepSitsInsideTheLifecycleNet
    {
        [Fact]
        public async Task ABedPoolFailureLeavesTheSpotRenderingAndTheGuardianReArmsItPastGrace()
        {
            // Given an approved spot and a bed pool that fails on its very next call...
            var harness = AdSpotWorkerHarness.Build(Now);
            harness.Store.AddSpot(1, AdState.Approved);
            harness.BedPool.ThrowOnNextCall = new InvalidOperationException("simulated bed pool failure");

            // When the worker ticks — the claim moves the row to rendering, the cast stamp succeeds,
            // and the bed pool throws before the render call is ever reached...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the tick's own outer catch swallowed the fault — the loop did not crash — and the
            // spot is left exactly where a crashed worker would leave it: stuck rendering, the render
            // call itself never even reached (the fake author was never invoked).
            Assert.Equal(AdState.Rendering, Assert.Single(harness.Store.Spots).State);
            Assert.Null(harness.Author.LastRequest);

            // When the guardian later sweeps, comfortably past its own grace (the FakeTimeProvider is
            // unrelated to the store's own real-wall-clock StateChangedAt stamps — jumped forward
            // relative to real UtcNow, not the harness's own fixed Now, so this holds regardless of
            // how long the tick above actually took to run)...
            harness.TimeProvider.SetUtcNow(DateTimeOffset.UtcNow.AddMinutes(20));
            await harness.Guardian.SweepOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then it re-arms back to approved for retry — the SAME lifecycle net every other stuck
            // render already gets, proving the bed step never opened a gap in it.
            Assert.Equal(AdState.Approved, Assert.Single(harness.Store.Spots).State);
            Assert.Equal(1, harness.Store.ReArmCallCount);
        }
    }
}
