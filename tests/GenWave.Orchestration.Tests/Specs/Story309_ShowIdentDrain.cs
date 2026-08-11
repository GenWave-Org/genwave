// STORY-309 — Show-branded idents (F117) — drain-preference half
//
// BDD specification — xUnit. Implemented at PLAN T250 via ProductionChainHarness.BuildProductionChain
// (Fakes/ProductionChainHarness.cs) — the shared T120 "real Orchestrator wired to a real
// CachingScheduleResolver/OnAirPersonaAccessor chain" harness this file's own facts were built
// against directly, rather than adding a would-be 4th inline BuildProductionChain copy (the review
// carry-forward recorded in PLAN). The pool-query half of STORY-309 lives in
// GenWave.MediaLibrary.Tests/Specs/Story309_ScopedImagingPool.cs.
//
// The cache-hit/rename-rekeys facts below pin what THIS layer is responsible for: producing
// IDENTICAL text-determining SegmentRequest fields (ShowName/StationName/Voice) across repeat drains
// for the same show, and DIFFERENT ones after a rename — the actual hash/forever-cache mechanics
// (TtsSegmentSource.ComputeHash) are a GenWave.Tts concern this project has no ProjectReference to and
// so cannot assert on directly; "same fields ⇒ same hash ⇒ cache hit" holds by construction.

namespace GenWave.Orchestration.Tests.Specs;

using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

public static class FeatureShowIdentDrain
{
    // -------------------------------------------------------------------------
    // Helpers (spec-local)
    // -------------------------------------------------------------------------

    static readonly DayOfWeek Monday = new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero).DayOfWeek;
    static readonly DateTimeOffset MidMorning = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);

    static readonly ShowSummary MorningShow =
        new(Id: 5, Name: "The Morning Mix", Tagline: "Wake up with us", Flavor: "bright, upbeat");

    static readonly ScheduleWeekSnapshot NoShows = new([]);

    /// <summary>One all-day, music-only (PersonaId null) block naming <paramref name="show"/> — just
    /// enough schedule for CachingScheduleResolver.TryGetCurrent() to answer with a Show; nothing else
    /// this file's facts need (no persona, no boundary within the run).</summary>
    static ScheduleWeekSnapshot AllDayShow(ShowSummary show) => new(
    [
        new ScheduleSegment(
            Id: 1, Day: Monday, StartMinute: 0, EndMinute: 1440, PersonaId: null,
            Genres: null, EnergyMin: null, EnergyMax: null, Show: show, ShowId: show.Id),
    ]);

    static MediaReference MakeImagingRef(string id) => new(
        id, $"/imaging/{id}.wav", "Station Ident", new Loudness(-14.0, -1.0, true),
        DurationMs: 5000, SampleRate: null, Channels: null, BitrateKbps: null,
        Artist: "Station Voice", Album: null, Genre: null, Year: null);

    static ProductionChainHarness.ProductionChain BuildChain(
        ScheduleWeekSnapshot snapshot, FakeMediaCatalog? catalog = null) =>
        ProductionChainHarness.BuildProductionChain(
            new FakePersonaStore(), snapshot, MidMorning, TimeSpan.Zero,
            catalog: catalog ?? new FakeMediaCatalog(ProductionChainHarness.MakeTrackRef("t1")));

    /// <summary>
    /// Drains the ONE remaining buffered item every unit here still owes (the music track itself —
    /// LeadIn/BackAnnounce are both off in <see cref="ProductionChainHarness.BuildProductionChain"/>'s
    /// default cadence, and no previous track exists yet on the very first unit either way) so the
    /// NEXT <see cref="Orchestrator.GetNextAsync"/> call assembles a genuinely NEW unit —
    /// <c>GetNextAsync</c>'s own first line serves any still-buffered item before planning anything,
    /// so a second drain enqueued without this would silently land on the SAME unit's leftover music
    /// item rather than ever reaching the deferral queue again.
    /// </summary>
    static Task DrainRestOfUnitAsync(Orchestrator orchestrator) =>
        orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

    public sealed class ScenarioScopedPoolFirst
    {
        [Fact]
        public async Task ScopedAuthoredRowAirsDuringItsShow()
        {
            // Given a ready authored station_id row scoped to the current show
            var authoredIdent = MakeImagingRef("42");
            var catalog = new FakeMediaCatalog(ProductionChainHarness.MakeTrackRef("t1"))
            {
                ImagingPoolResult = authoredIdent,
            };
            var chain = BuildChain(AllDayShow(MorningShow), catalog);
            chain.Queue.Enqueue(SpeechDeferralKind.StationId, "test");

            // When the StationId drain fires during that show...
            var item = await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Then the scoped row airs (authored voice preserved — it is rendered audio, no TTS)
            Assert.NotNull(item);
            Assert.Equal(authoredIdent.MediaId, item!.MediaId);
            Assert.Equal(SegmentKind.StationId, item.SegmentKind);
            Assert.Empty(chain.Tts.Requests);

            // The current show reached the pool query (the ladder's own scoped-first rung).
            var call = Assert.Single(chain.Catalog.ImagingKindCalls);
            Assert.Equal(MorningShow.Id, call.ShowId);
        }
    }

    public sealed class ScenarioTemplatedFloor
    {
        [Fact]
        public async Task TemplatedShowLineAirsWhenNoScopedRows()
        {
            // Given a show with zero scoped authored rows — the pool call finds nothing at all for
            // this show (ImagingPoolResult left null)
            var chain = BuildChain(AllDayShow(MorningShow));
            chain.Queue.Enqueue(SpeechDeferralKind.StationId, "test");

            // When the drain fires...
            var item = await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Then "You're listening to {show} on {station}." renders — station-voiced, zero LLM (the
            // gate-countable floor, F117.2): the SAME SegmentKind.StationId request shape, with
            // ShowName additionally stamped. This project has no ProjectReference to GenWave.Tts, so
            // the literal spoken text PatterTemplateRenderer.Expand produces from that stamp is pinned
            // over there instead — GenWave.Tts.Tests/Specs/Story006_PatterTemplates.cs's
            // ScenarioStationIdTemplate.ShowNameStampsTheShowBrandedLine (PLAN T250 review finding F3).
            // This fact stops at proving the ORCHESTRATOR stamped the right ShowName/StationName/
            // Voice/PersonaName onto the request handed to TTS.
            Assert.NotNull(item);
            var request = Assert.Single(chain.Tts.Requests, r => r.Kind == SegmentKind.StationId);
            Assert.Equal(MorningShow.Name, request.ShowName);
            Assert.Equal("GenWave", request.StationName);
            Assert.Equal("default", request.Voice); // the station's own identity voice, gh-#96
            Assert.Null(request.PersonaName); // never persona-voiced, never LLM-authored
        }

        [Fact]
        public async Task SecondAiringIsACacheHit()
        {
            // Given the templated show line rendered once...
            var chain = BuildChain(AllDayShow(MorningShow));
            chain.Queue.Enqueue(SpeechDeferralKind.StationId, "test");
            await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            var first = Assert.Single(chain.Tts.Requests, r => r.Kind == SegmentKind.StationId);
            await DrainRestOfUnitAsync(chain.Orchestrator); // the music track — empties the buffer

            // When the drain fires again for the same show name...
            chain.Queue.Enqueue(SpeechDeferralKind.StationId, "test");
            await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // A genuine second StationId render happened (PLAN T250 review finding F4) — without this,
            // Last() below could alias back onto `first` itself (e.g. if the second drain silently
            // produced nothing new) and the field comparisons that follow would pass vacuously.
            Assert.Equal(2, chain.Tts.Requests.Count(r => r.Kind == SegmentKind.StationId));
            var second = chain.Tts.Requests.Last(r => r.Kind == SegmentKind.StationId);

            // Then the render is a forever-cache hit keyed on the rendered text (F110.3's own
            // precedent, TtsSegmentSource.ComputeHash): the Orchestrator's own responsibility is
            // producing IDENTICAL text-determining fields on both drains, which is what makes that
            // hash — and so the cache hit — identical by construction.
            Assert.Equal(first.ShowName, second.ShowName);
            Assert.Equal(first.StationName, second.StationName);
            Assert.Equal(first.Voice, second.Voice);
        }

        [Fact]
        public async Task RenameRekeysTheCache()
        {
            // Given the show is renamed...
            var chain = BuildChain(AllDayShow(MorningShow));
            chain.Queue.Enqueue(SpeechDeferralKind.StationId, "test");
            await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            var before = Assert.Single(chain.Tts.Requests, r => r.Kind == SegmentKind.StationId);
            await DrainRestOfUnitAsync(chain.Orchestrator); // the music track — empties the buffer

            var renamed = MorningShow with { Name = "The Sunrise Session" };
            chain.ScheduleStore.SetSnapshot(AllDayShow(renamed));
            chain.ScheduleStore.RaiseWeekChanged();

            // When the next drain fires...
            chain.Queue.Enqueue(SpeechDeferralKind.StationId, "test");
            await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            var after = chain.Tts.Requests.Last(r => r.Kind == SegmentKind.StationId);

            // Then a fresh render occurs by construction — the key IS the text, and the text now
            // names the renamed show.
            Assert.Equal(MorningShow.Name, before.ShowName);
            Assert.Equal(renamed.Name, after.ShowName);
            Assert.NotEqual(before.ShowName, after.ShowName);
        }
    }

    public sealed class ScenarioOutsideShowsUntouched
    {
        [Fact]
        public async Task NoShowMeansF110Exactly()
        {
            // Given no show on the air...
            var chain = BuildChain(NoShows);
            chain.Queue.Enqueue(SpeechDeferralKind.StationId, "test");

            // When the StationId drain fires...
            var item = await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Then behavior is byte-identical to F110.2 (station pool → template): the plain templated
            // ident, no ShowName ever stamped.
            Assert.NotNull(item);
            var request = Assert.Single(chain.Tts.Requests, r => r.Kind == SegmentKind.StationId);
            Assert.Null(request.ShowName);
            Assert.Null(request.PersonaName);
            Assert.Equal("default", request.Voice);

            // No show ⇒ the pool query runs scoped to nothing (showId null), F110.2's own arg shape.
            var call = Assert.Single(chain.Catalog.ImagingKindCalls);
            Assert.Null(call.ShowId);
        }
    }
}
