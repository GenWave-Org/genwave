// STORY-297 — Context segments air at boundaries (F107.3, F107.4, F107.7)
//
// BDD specification — xUnit. T224's facts cover the ORCHESTRATOR's own drain arm and persona
// resolution — a real Orchestrator wired to fakes at the store/tts/clock/settings seams, mirroring
// Story136_StationIdCadence.cs's harness idiom (no CachingScheduleResolver needed: context segments
// don't depend on the format-clock schedule). The news-posture prompt wording and blurbs-cache
// routing are Tts-level facts this project cannot see (no ProjectReference to GenWave.Tts) — they
// live in GenWave.Tts.Tests/Specs/Story297_ContextSegmentsAir.cs instead, mirroring Story243's own
// split (see that file's header).

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureContextSegmentsAirAtBoundaries
{
    static MediaReference MakeTrackRef(string id) => new(
        id,
        $"/media/{id}.mp3",
        $"Track {id}",
        new Loudness(-23.0, -1.0, true),
        null, null, null, null, null, null, null, null);

    static Persona MakePersona(long id, string name, string voice)
    {
        var now = DateTime.UnixEpoch;
        return new Persona(id, name, "", "", voice, now, now);
    }

    static bool IsContextSegment(MediaItem item) =>
        item.MediaId.StartsWith("tts:contextsegment", StringComparison.OrdinalIgnoreCase);

    static bool IsMusic(MediaItem item) =>
        !item.MediaId.StartsWith("tts:", StringComparison.Ordinal);

    static Orchestrator BuildOrchestrator(
        SpeechDeferralQueue queue,
        TimeProvider clock,
        FakeTtsSegmentSource tts,
        ILogger<Orchestrator>? logger = null,
        FakeActivePersonaAccessor? personaAccessor = null,
        FakeContextSettingsProvider? contextSettings = null,
        FakePersonaStore? personaStore = null)
    {
        var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default"));
        var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
        var cadenceProvider = new FakeCadenceProvider(new CadenceConfig
        {
            LeadInBeforeEachTrack = false,
            BackAnnounceAfterEachTrack = false,
            StationIdEveryNUnits = 0,
        });
        var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings());
        var catalog = new FakeMediaCatalog(MakeTrackRef("t1"));
        var musicSelectionPolicy = new MusicSelectionPolicy(catalog, NullLogger<MusicSelectionPolicy>.Instance);

        return new Orchestrator(
            identityProvider, scopeProvider, cadenceProvider, rotationProvider, musicSelectionPolicy, tts,
            personaAccessor ?? new FakeActivePersonaAccessor(),
            logger ?? NullLogger<Orchestrator>.Instance,
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)),
            queue,
            clock,
            new FakeBoundaryBiasProvider(TimeSpan.Zero),
            personaStore: personaStore,
            contextSettings: contextSettings ?? new FakeContextSettingsProvider());
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioADueSegmentAirsAtTheBoundary
    {
        [Fact]
        public async Task AnEnqueuedContextDeferralDrainsAtTheNextUnitSeam()
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-08T00:00:00Z"));
            var queue = new SpeechDeferralQueue(clock);
            var tts = new FakeTtsSegmentSource();
            var orchestrator = BuildOrchestrator(queue, clock, tts);

            var content = new ContextSegmentFacts(
                "Sunny and seventy-two degrees.", clock.GetUtcNow().AddHours(1));
            queue.Enqueue(SpeechDeferralKind.Context, "cadence elapsed", discriminator: "weather", context: content);

            // The first pull drains the due context deferral — it lands in the buffer AHEAD of the
            // music track the same unit plans (F74.1: a whole unit is queued atomically).
            var first = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            var second = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(first);
            Assert.True(IsContextSegment(first!), "the context segment must air before the track");
            Assert.NotNull(second);
            Assert.True(IsMusic(second!), "the buffered track follows the context segment");
        }

        [Fact]
        public async Task TheCopyRequestCarriesProviderFactsWithTheNewsPosture()
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-08T00:00:00Z"));
            var queue = new SpeechDeferralQueue(clock);
            var tts = new FakeTtsSegmentSource();
            var orchestrator = BuildOrchestrator(queue, clock, tts);

            const string Facts = "Sunny and seventy-two degrees, light breeze from the west.";
            var content = new ContextSegmentFacts(Facts, clock.GetUtcNow().AddHours(1));
            queue.Enqueue(SpeechDeferralKind.Context, "cadence elapsed", discriminator: "weather", context: content);

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // The prompt's own "do not add facts" news-posture wording, and the FreshPerAiring
            // blurbs-cache routing, are Tts-level facts (LlmPromptBuilder/TtsSegmentSource) this
            // project cannot see — proven in GenWave.Tts.Tests/Specs/Story297_ContextSegmentsAir.cs
            // instead. This project's own seam is the SegmentRequest the Orchestrator hands down.
            var request = Assert.Single(tts.Requests, r => r.Kind == SegmentKind.ContextSegment);
            Assert.Equal(Facts, request.ContextFacts);
        }
    }

    public sealed class ScenarioPersonaAssignment
    {
        [Fact]
        public async Task PersonaIdZeroRendersInTheOnAirVoice()
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-08T00:00:00Z"));
            var queue = new SpeechDeferralQueue(clock);
            var tts = new FakeTtsSegmentSource();
            var accessor = new FakeActivePersonaAccessor { Persona = MakePersona(10, "DJ Alpha", "af_alpha") };
            var contextSettings = new FakeContextSettingsProvider();
            contextSettings.Set("weather", new ContextProviderSettings(Enabled: true, SegmentCadenceMinutes: 60, PatterCadenceMinutes: 30, PersonaId: 0));
            var orchestrator = BuildOrchestrator(
                queue, clock, tts, personaAccessor: accessor, contextSettings: contextSettings);

            var content = new ContextSegmentFacts("Sunny.", clock.GetUtcNow().AddHours(1));
            queue.Enqueue(SpeechDeferralKind.Context, "cadence elapsed", discriminator: "weather", context: content);

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            var request = Assert.Single(tts.Requests, r => r.Kind == SegmentKind.ContextSegment);
            Assert.Equal("af_alpha", request.Voice);
            Assert.Equal("DJ Alpha", request.PersonaName);
        }

        [Fact]
        public async Task MusicOnlySegmentsRenderInTheStationVoice()
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-08T00:00:00Z"));
            var queue = new SpeechDeferralQueue(clock);
            var tts = new FakeTtsSegmentSource();
            var contextSettings = new FakeContextSettingsProvider();
            contextSettings.Set("weather", new ContextProviderSettings(Enabled: true, SegmentCadenceMinutes: 60, PatterCadenceMinutes: 30, PersonaId: 0));
            // No accessor persona set (defaults null) — the music-only-segment-or-gap shape (F107.7).
            var orchestrator = BuildOrchestrator(queue, clock, tts, contextSettings: contextSettings);

            var content = new ContextSegmentFacts("Sunny.", clock.GetUtcNow().AddHours(1));
            queue.Enqueue(SpeechDeferralKind.Context, "cadence elapsed", discriminator: "weather", context: content);

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            var request = Assert.Single(tts.Requests, r => r.Kind == SegmentKind.ContextSegment);
            Assert.Equal("default", request.Voice); // the station's own identity voice (StationId precedent)
            Assert.Null(request.PersonaName);
        }

        [Fact]
        public async Task ExplicitPersonaIdRendersInThatPersonasVoice()
        {
            // Additive coverage beyond the 5 T224 facts: PersonaId > 0 is the third rung of F107.7's
            // resolution table (explicit persona, resolved through IPersonaStore) and is otherwise
            // untested at any level.
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-08T00:00:00Z"));
            var queue = new SpeechDeferralQueue(clock);
            var tts = new FakeTtsSegmentSource();
            var store = new FakePersonaStore();
            store.Add(MakePersona(42, "Roxy Static", "af_roxy"));
            var contextSettings = new FakeContextSettingsProvider();
            contextSettings.Set("weather", new ContextProviderSettings(Enabled: true, SegmentCadenceMinutes: 60, PatterCadenceMinutes: 30, PersonaId: 42));
            var orchestrator = BuildOrchestrator(
                queue, clock, tts, contextSettings: contextSettings, personaStore: store);

            var content = new ContextSegmentFacts("Sunny.", clock.GetUtcNow().AddHours(1));
            queue.Enqueue(SpeechDeferralKind.Context, "cadence elapsed", discriminator: "weather", context: content);

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            var request = Assert.Single(tts.Requests, r => r.Kind == SegmentKind.ContextSegment);
            Assert.Equal("af_roxy", request.Voice);
            Assert.Equal("Roxy Static", request.PersonaName);
        }
    }

    public sealed class ScenarioPerProviderSupersede
    {
        [Fact]
        public void WeatherAndHistoryPendingTogetherBothDrain()
        {
            // Two ContextSegment deferrals with different discriminators coexist — supersede is
            // per (kind, discriminator), so a due weather fact never silently discards a due
            // history fact (SPEC F107.4).
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-08T00:00:00Z"));
            var queue = new SpeechDeferralQueue(clock);

            queue.Enqueue(SpeechDeferralKind.Context, "weather cadence elapsed", discriminator: "weather");
            queue.Enqueue(SpeechDeferralKind.Context, "history cadence elapsed", discriminator: "history");

            var drained = queue.TryDequeueDue(clock.GetUtcNow());

            Assert.Equal(2, drained.Count);
            Assert.Contains(drained, deferral => deferral.Discriminator == "weather");
            Assert.Contains(drained, deferral => deferral.Discriminator == "history");
            Assert.Null(queue.NextDue); // both consumed — nothing left leaking into a later boundary
        }

        [Fact]
        public void TwoWeatherDeferralsCollapseToTheNewer()
        {
            // Same (kind, discriminator) pair still supersedes (F74.2 semantics, now scoped to the
            // pair rather than the bare kind): the older weather deferral is discarded at the second
            // Enqueue and never reaches the drain at all.
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-08T00:00:00Z"));
            var queue = new SpeechDeferralQueue(clock);

            queue.Enqueue(SpeechDeferralKind.Context, "stale weather", discriminator: "weather");
            clock.Advance(TimeSpan.FromMinutes(5)); // still mid-track — nothing has drained yet
            queue.Enqueue(SpeechDeferralKind.Context, "fresh weather", discriminator: "weather");

            var drained = queue.TryDequeueDue(clock.GetUtcNow());

            var aired = Assert.Single(drained);
            Assert.Equal("fresh weather", aired.Reason);
            Assert.Equal("weather", aired.Discriminator);
        }

        [Fact]
        public void NullDiscriminatorKindsBehaveExactlyAsToday()
        {
            // Pins the byte-identical claim (SPEC F107.4): every kind that predates F107 (StationId
            // here) always enqueues with a null discriminator, so it drives the SAME supersede code
            // path Story197_SpeechBoundaryDeferral's ScenarioSupersede fact already covers — this
            // reproduces that exact scenario through the (kind, discriminator) seam and additionally
            // pins that the surviving entry's own Discriminator reads back null.
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-08T00:00:00Z"));
            var queue = new SpeechDeferralQueue(clock);

            queue.Enqueue(SpeechDeferralKind.StationId, "stale ident");
            clock.Advance(TimeSpan.FromMinutes(5)); // still mid-track — the long track hasn't ended
            queue.Enqueue(SpeechDeferralKind.StationId, "fresh ident");

            var due = queue.TryDequeueDue(clock.GetUtcNow());

            var aired = Assert.Single(due);
            Assert.Equal("fresh ident", aired.Reason);
            Assert.Null(aired.Discriminator);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAFailedRenderNeverBlocksMusic
    {
        [Fact]
        public async Task ANullRenderDropsTheSegmentAndMusicContinues()
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-08T00:00:00Z"));
            var queue = new SpeechDeferralQueue(clock);
            var tts = new FakeTtsSegmentSource { ShouldReturnNull = r => r.Kind == SegmentKind.ContextSegment };
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(queue, clock, tts, logger: logger);

            var content = new ContextSegmentFacts("Sunny.", clock.GetUtcNow().AddHours(1));
            queue.Enqueue(SpeechDeferralKind.Context, "cadence elapsed", discriminator: "weather", context: content);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // ITtsSegmentSource returned null — no context item reaches the buffer, the music unit
            // (the ONLY item this pull returns) is untouched.
            Assert.NotNull(item);
            Assert.True(IsMusic(item!));
            Assert.False(IsContextSegment(item!));

            // The render was genuinely ATTEMPTED (not skipped upstream at the freshness check)...
            Assert.Contains(tts.Requests, r => r.Kind == SegmentKind.ContextSegment);
            // ...and the drop is recorded, naming the drop (F107.6) rather than staying silent.
            Assert.Contains(
                logger.Entries,
                e => e.Level >= LogLevel.Information
                    && e.Message.Contains("Context segment", StringComparison.OrdinalIgnoreCase));
        }
    }

    // T224 review finding (R2): the two drain-time re-checks — stale FreshUntil, blank
    // SegmentFacts — sat at zero coverage; both skip BEFORE the render is ever attempted (unlike
    // ScenarioAFailedRenderNeverBlocksMusic above, whose drop happens AFTER a genuine render), so
    // neither prior fact could have caught either guard being deleted or inverted. Each fact below
    // was verified RED by temporarily commenting its own guard at Orchestrator.cs (then restoring
    // byte-identical, confirmed via `git diff`).
    public sealed class ScenarioDrainTimeGuardsSkipBeforeEverRendering
    {
        [Fact]
        public async Task AStaleFreshUntilNeverReachesTheRendererAndMusicAirs()
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-08T00:00:00Z"));
            var queue = new SpeechDeferralQueue(clock);
            var tts = new FakeTtsSegmentSource();
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(queue, clock, tts, logger: logger);

            // FreshUntil already in the past AT ENQUEUE TIME — the drain-time re-check (SPEC
            // F107.3/F107.6, Orchestrator.cs's own "content.FreshUntil <= drainNow" guard) must
            // catch this itself; nothing upstream filters it out before the drain loop sees it.
            var content = new ContextSegmentFacts("Sunny.", clock.GetUtcNow().AddMinutes(-1));
            queue.Enqueue(SpeechDeferralKind.Context, "cadence elapsed", discriminator: "weather", context: content);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.True(IsMusic(item!));

            // Never even reaches ITtsSegmentSource — this is a BEFORE-render skip, not an
            // after-render drop (contrast ScenarioAFailedRenderNeverBlocksMusic above).
            Assert.DoesNotContain(tts.Requests, r => r.Kind == SegmentKind.ContextSegment);

            // The Information line names the originating provider, not just "a context segment".
            Assert.Contains(
                logger.Entries,
                e => e.Level == LogLevel.Information
                    && e.Message.Contains("weather", StringComparison.Ordinal)
                    && e.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task BlankSegmentFactsNeverReachTheRendererAndMusicAirs()
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-08T00:00:00Z"));
            var queue = new SpeechDeferralQueue(clock);
            var tts = new FakeTtsSegmentSource();
            var logger = new CapturingLogger<Orchestrator>();
            var orchestrator = BuildOrchestrator(queue, clock, tts, logger: logger);

            // A successful fetch that produced no SegmentFacts (T222 ruling: "no segment lane this
            // fetch", not a failure) — the drain-time "string.IsNullOrWhiteSpace(content.SegmentFacts)"
            // guard must catch this itself, independent of the freshness guard right above it.
            var content = new ContextSegmentFacts("   ", clock.GetUtcNow().AddHours(1));
            queue.Enqueue(SpeechDeferralKind.Context, "cadence elapsed", discriminator: "weather", context: content);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.True(IsMusic(item!));

            Assert.DoesNotContain(tts.Requests, r => r.Kind == SegmentKind.ContextSegment);

            Assert.Contains(
                logger.Entries,
                e => e.Level == LogLevel.Information
                    && e.Message.Contains("weather", StringComparison.Ordinal)
                    && e.Message.Contains("segment facts", StringComparison.OrdinalIgnoreCase));
        }
    }
}
