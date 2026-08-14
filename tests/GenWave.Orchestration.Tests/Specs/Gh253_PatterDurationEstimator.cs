// gh-#253 — Patter duration is unknown at planning time: the estimator seam
//
// BDD specification — xUnit. Drives RollingPatterDurationEstimator's three honest tiers directly,
// plus the Orchestrator's ObserveRendered feed (a successful render's MEASURED F66.1 duration —
// and only a measured one — flows back into the seam). No behavior change is asserted anywhere:
// gh-#253 only exposes numbers; the boundary-fit consumer is gh-#254's spec.

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeaturePatterDurationEstimator
{
    public static class ScenarioColdHeuristicTier
    {
        [Fact]
        public static void An_unobserved_llm_kind_estimates_at_the_heuristic_tier()
        {
            // Given a cold estimator with no observations at all
            var estimator = new RollingPatterDurationEstimator();

            // When an LLM-authored kind is estimated
            var estimate = estimator.Estimate(SegmentKind.SignOff, "Flip", "af_heart");

            // Then the answer is the chars-per-second heuristic — a plausible spoken-blurb length,
            // honestly labeled as the lowest tier.
            Assert.Equal(PatterEstimateConfidence.Heuristic, estimate.Confidence);
            Assert.InRange(estimate.Duration, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20));
        }

        [Fact]
        public static void The_live_MaxCopyChars_bounds_the_llm_worst_case()
        {
            // Given an operator who capped Llm:MaxCopyChars at 60 chars
            var estimator = new RollingPatterDurationEstimator(new FakeCopyBoundsProvider(60));

            // When an LLM-authored kind is estimated cold
            var estimate = estimator.Estimate(SegmentKind.LeadIn, personaName: null, "af_heart");

            // Then the estimate shrinks with the bound (60 chars at ~15 chars/s = 4s), never the
            // uncapped typical-copy guess.
            Assert.Equal(PatterEstimateConfidence.Heuristic, estimate.Confidence);
            Assert.Equal(TimeSpan.FromSeconds(4), estimate.Duration);
        }

        [Fact]
        public static void Estimates_never_go_non_positive()
        {
            // Given an absurdly tight copy bound (1 char)
            var estimator = new RollingPatterDurationEstimator(new FakeCopyBoundsProvider(1));

            // When any kind is estimated
            var estimate = estimator.Estimate(SegmentKind.BackAnnounce, "Flip", "af_heart");

            // Then the heuristic floor holds — even a one-word clip takes real air time.
            Assert.True(estimate.Duration >= TimeSpan.FromSeconds(2));
        }
    }

    public static class ScenarioHistoricalTier
    {
        [Fact]
        public static void Three_observed_renders_promote_the_key_to_a_historical_average()
        {
            // Given three measured sign-off durations for one persona
            var estimator = new RollingPatterDurationEstimator();
            estimator.ObserveRendered(SegmentKind.SignOff, "Flip", "af_heart", TimeSpan.FromSeconds(10));
            estimator.ObserveRendered(SegmentKind.SignOff, "Flip", "af_heart", TimeSpan.FromSeconds(20));
            estimator.ObserveRendered(SegmentKind.SignOff, "Flip", "af_heart", TimeSpan.FromSeconds(30));

            // When that persona × kind is estimated
            var estimate = estimator.Estimate(SegmentKind.SignOff, "Flip", "af_heart");

            // Then the rolling average rides out at the historical tier.
            Assert.Equal(PatterEstimateConfidence.Historical, estimate.Confidence);
            Assert.Equal(TimeSpan.FromSeconds(20), estimate.Duration);
        }

        [Fact]
        public static void Fewer_than_three_samples_use_the_average_but_stay_at_the_heuristic_tier()
        {
            // Given a single measured sample — real data, but one point is not a trend
            var estimator = new RollingPatterDurationEstimator();
            estimator.ObserveRendered(SegmentKind.LeadIn, "Flip", "af_heart", TimeSpan.FromSeconds(14));

            // When estimated
            var estimate = estimator.Estimate(SegmentKind.LeadIn, "Flip", "af_heart");

            // Then the measured value beats the chars guess, honestly labeled low-confidence.
            Assert.Equal(PatterEstimateConfidence.Heuristic, estimate.Confidence);
            Assert.Equal(TimeSpan.FromSeconds(14), estimate.Duration);
        }

        [Fact]
        public static void History_is_keyed_per_persona_and_kind()
        {
            // Given one persona's observed history
            var estimator = new RollingPatterDurationEstimator();
            estimator.ObserveRendered(SegmentKind.SignOff, "Flip", "af_heart", TimeSpan.FromSeconds(10));
            estimator.ObserveRendered(SegmentKind.SignOff, "Flip", "af_heart", TimeSpan.FromSeconds(10));
            estimator.ObserveRendered(SegmentKind.SignOff, "Flip", "af_heart", TimeSpan.FromSeconds(10));

            // When a DIFFERENT persona (and a different kind) is estimated
            var otherPersona = estimator.Estimate(SegmentKind.SignOff, "Mic Cardioid", "am_michael");
            var otherKind = estimator.Estimate(SegmentKind.LeadIn, "Flip", "af_heart");

            // Then neither borrows Flip's sign-off history — both stay cold.
            Assert.Equal(PatterEstimateConfidence.Heuristic, otherPersona.Confidence);
            Assert.Equal(PatterEstimateConfidence.Heuristic, otherKind.Confidence);
        }

        [Fact]
        public static void A_non_positive_observation_is_never_recorded()
        {
            // Given a zero-length "measurement" (F66.1: measured, never fabricated — zero is neither)
            var estimator = new RollingPatterDurationEstimator();
            estimator.ObserveRendered(SegmentKind.SignOff, "Flip", "af_heart", TimeSpan.Zero);

            // When estimated
            var estimate = estimator.Estimate(SegmentKind.SignOff, "Flip", "af_heart");

            // Then the key is still cold — the bogus sample never entered the history.
            Assert.Equal(PatterEstimateConfidence.Heuristic, estimate.Confidence);
            Assert.True(estimate.Duration >= TimeSpan.FromSeconds(2));
        }
    }

    public static class ScenarioExactTier
    {
        [Fact]
        public static void A_station_id_observation_is_exact_for_that_voice()
        {
            // Given one measured station-ID render — its copy is deterministic per (station, voice),
            // so the cached clip replays verbatim on every future airing
            var estimator = new RollingPatterDurationEstimator();
            estimator.ObserveRendered(SegmentKind.StationId, personaName: null, "af_heart", TimeSpan.FromSeconds(7));

            // When the same voice's station ID is estimated
            var estimate = estimator.Estimate(SegmentKind.StationId, personaName: null, "af_heart");

            // Then the answer IS the measured duration, at the exact tier.
            Assert.Equal(PatterEstimateConfidence.Exact, estimate.Confidence);
            Assert.Equal(TimeSpan.FromSeconds(7), estimate.Duration);
        }

        [Fact]
        public static void A_different_voice_never_reuses_another_voices_exact_clip()
        {
            // Given a station-ID measurement under one voice
            var estimator = new RollingPatterDurationEstimator();
            estimator.ObserveRendered(SegmentKind.StationId, personaName: null, "af_heart", TimeSpan.FromSeconds(7));

            // When a DIFFERENT voice's station ID is estimated (a live Station:Voice edit re-keys
            // the TTS cache and re-renders — the old measurement proves nothing about the new clip)
            var estimate = estimator.Estimate(SegmentKind.StationId, personaName: null, "am_michael");

            // Then the exact tier does not fire for it.
            Assert.NotEqual(PatterEstimateConfidence.Exact, estimate.Confidence);
        }
    }

    // SPEC F117.2, PLAN T250 review finding F1 — the templated show line varies the StationId
    // render's TEXT (and so its measured duration) by on-air show; the Exact memo must key on
    // (voice, show) rather than voice alone, or one show's measurement corrupts another's — and the
    // plain (showless) ident's — Exact answer.
    public static class ScenarioShowBrandedExactTier
    {
        [Fact]
        public static void A_show_branded_observation_is_exact_only_for_that_shows_own_estimate()
        {
            // Given one measured show-branded station-ID render for "The Morning Mix"
            var estimator = new RollingPatterDurationEstimator();
            estimator.ObserveRendered(
                SegmentKind.StationId, personaName: null, "af_heart", TimeSpan.FromSeconds(9), showName: "The Morning Mix");

            // When that SAME show is estimated
            var estimate = estimator.Estimate(SegmentKind.StationId, personaName: null, "af_heart", showName: "The Morning Mix");

            // Then the answer IS the measured duration, at the exact tier.
            Assert.Equal(PatterEstimateConfidence.Exact, estimate.Confidence);
            Assert.Equal(TimeSpan.FromSeconds(9), estimate.Duration);
        }

        [Fact]
        public static void MixedShowDurationsNeverCrossContaminateTheExactAnswer()
        {
            // Given two DIFFERENT shows' station-ID renders measured under the SAME voice, plus a
            // plain (showless) ident measured under that same voice too
            var estimator = new RollingPatterDurationEstimator();
            estimator.ObserveRendered(
                SegmentKind.StationId, personaName: null, "af_heart", TimeSpan.FromSeconds(9), showName: "The Morning Mix");
            estimator.ObserveRendered(
                SegmentKind.StationId, personaName: null, "af_heart", TimeSpan.FromSeconds(5), showName: "Night Moves");
            estimator.ObserveRendered(
                SegmentKind.StationId, personaName: null, "af_heart", TimeSpan.FromSeconds(3), showName: null);

            // When each is estimated by its OWN (voice, show) key
            var morning = estimator.Estimate(SegmentKind.StationId, personaName: null, "af_heart", showName: "The Morning Mix");
            var night = estimator.Estimate(SegmentKind.StationId, personaName: null, "af_heart", showName: "Night Moves");
            var plain = estimator.Estimate(SegmentKind.StationId, personaName: null, "af_heart", showName: null);

            // Then each answer is its OWN exact measurement — none leaks into another's.
            Assert.Equal(PatterEstimateConfidence.Exact, morning.Confidence);
            Assert.Equal(TimeSpan.FromSeconds(9), morning.Duration);
            Assert.Equal(PatterEstimateConfidence.Exact, night.Confidence);
            Assert.Equal(TimeSpan.FromSeconds(5), night.Duration);
            Assert.Equal(PatterEstimateConfidence.Exact, plain.Confidence);
            Assert.Equal(TimeSpan.FromSeconds(3), plain.Duration);
        }

        [Fact]
        public static void TheShowAwareOverloadAnswersFromAShowBrandedObservationGivenTheMatchingShow()
        {
            // Given ONLY a show-branded observation for this voice (no plain ident ever measured) —
            // BuildBoundaryFit (gh-#463) reads its scheduleResolver's on-air show one expression away
            // and calls this SAME 4-arg overload with it, rather than the show-blind 3-arg call it used
            // to make
            var estimator = new RollingPatterDurationEstimator();
            estimator.ObserveRendered(
                SegmentKind.StationId, personaName: null, "af_heart", TimeSpan.FromSeconds(9), showName: "The Morning Mix");

            // When the show-aware 4-arg overload estimates the same voice with the matching show name
            var estimate = estimator.Estimate(SegmentKind.StationId, personaName: null, "af_heart", showName: "The Morning Mix");

            // Then it answers the show-branded Exact duration — the fit no longer under-estimates by
            // consulting a different (voice, show) bucket's duration for the airing that will actually
            // render.
            Assert.Equal(PatterEstimateConfidence.Exact, estimate.Confidence);
            Assert.Equal(TimeSpan.FromSeconds(9), estimate.Duration);
        }
    }

    public static class ScenarioOrchestratorFeedsTheSeam
    {
        static MediaReference MakeTrack(string id) => new(
            MediaId: id,
            Locator: $"/media/{id}.mp3",
            Title: $"Track {id}",
            Loudness: new Loudness(-23.0, -1.0, true),
            DurationMs: 180_000,
            SampleRate: null,
            Channels: null,
            BitrateKbps: null,
            Artist: null,
            Album: null,
            Genre: null,
            Year: null);

        [Fact]
        public static async Task Rendered_segment_durations_flow_into_the_estimator()
        {
            // Given a cadence that renders a lead-in per unit, with measured 12s renders
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2030-01-01T00:00:00Z"));
            var estimator = new RollingPatterDurationEstimator();
            var tts = new FakeTtsSegmentSource { DurationMs = 12_000 };
            var orchestrator = new Orchestrator(
                new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default")),
                new FakeStationScopeProvider(new LibraryScope([1L])),
                new FakeCadenceProvider(new CadenceConfig
                {
                    LeadInBeforeEachTrack = true,
                    BackAnnounceAfterEachTrack = false,
                    StationIdEveryNUnits = 0,
                }),
                new FakeRotationSettingsProvider(new RotationSettings()),
                new MusicSelectionPolicy(new FakeMediaCatalog(MakeTrack("m1")), NullLogger<MusicSelectionPolicy>.Instance),
                tts,
                new FakeActivePersonaAccessor(),
                NullLogger<Orchestrator>.Instance,
                new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)),
                new SpeechDeferralQueue(clock),
                clock,
                new FakeBoundaryBiasProvider(TimeSpan.FromMinutes(10)),
                patterEstimator: estimator);

            // When three units are planned (each unit = one lead-in + one music item)
            var ctx = new PlayoutContext([]);
            for (var i = 0; i < 6; i++) await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            // Then the estimator's lead-in key reached the historical tier at the measured value.
            var estimate = estimator.Estimate(SegmentKind.LeadIn, personaName: null, "default");
            Assert.Equal(PatterEstimateConfidence.Historical, estimate.Confidence);
            Assert.Equal(TimeSpan.FromSeconds(12), estimate.Duration);
        }

        [Fact]
        public static async Task A_render_without_a_measured_duration_is_never_observed()
        {
            // Given renders that complete WITHOUT a duration (cue analysis failed — F66.1 stays null)
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2030-01-01T00:00:00Z"));
            var estimator = new RollingPatterDurationEstimator();
            var tts = new FakeTtsSegmentSource { DurationMs = null };
            var orchestrator = new Orchestrator(
                new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default")),
                new FakeStationScopeProvider(new LibraryScope([1L])),
                new FakeCadenceProvider(new CadenceConfig
                {
                    LeadInBeforeEachTrack = true,
                    BackAnnounceAfterEachTrack = false,
                    StationIdEveryNUnits = 0,
                }),
                new FakeRotationSettingsProvider(new RotationSettings()),
                new MusicSelectionPolicy(new FakeMediaCatalog(MakeTrack("m1")), NullLogger<MusicSelectionPolicy>.Instance),
                tts,
                new FakeActivePersonaAccessor(),
                NullLogger<Orchestrator>.Instance,
                new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)),
                new SpeechDeferralQueue(clock),
                clock,
                new FakeBoundaryBiasProvider(TimeSpan.FromMinutes(10)),
                patterEstimator: estimator);

            // When units are planned
            var ctx = new PlayoutContext([]);
            for (var i = 0; i < 6; i++) await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            // Then nothing was fabricated into the history — the key stays cold.
            var estimate = estimator.Estimate(SegmentKind.LeadIn, personaName: null, "default");
            Assert.Equal(PatterEstimateConfidence.Heuristic, estimate.Confidence);
        }
    }
}
