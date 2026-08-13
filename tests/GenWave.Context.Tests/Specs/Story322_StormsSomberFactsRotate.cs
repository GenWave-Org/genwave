// STORY-322 — Storms stay somber, facts rotate (gh-#468 · SPEC F125 · PLAN VQ-g, T271–T272)
//
// BDD specification — xUnit, pending until /build-loop turns them green. Two faults, one
// story: the somber vocabulary has no wind-storm family (a tornado touchdown aired as
// chill-morning color), and the vend path has no per-fact memory at all — BuildContent's
// chosen[0] is deterministic, so the SAME patter fact vends all day and the SAME 4-fact
// segment string every slot. Rotation moves selection to vend time over the airable list.
// One assertion per Fact; happy first; sad segregated. T273's wire acceptance (distinct
// facts audible over 3+ slots on a running stack) is a production check, not here.

using GenWave.Context.History;
using GenWave.Context.Tests.Fakes;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Context.Tests.Specs;

public static class FeatureStormsSomberFactsRotate
{
    // A fixed, cadence-aligned instant (an exact UTC midnight) so every scenario's cadence-slot math
    // is deterministic regardless of the wall clock the test happened to run at — mirrors
    // Story296_ContextPipeline.cs's own StartTime idiom.
    static FakeTimeProvider NewTime() => new(new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero));

    static FakeContextSettingsProvider EnabledSettings(
        string key, int segmentCadenceMinutes, int patterCadenceMinutes)
    {
        var settings = new FakeContextSettingsProvider();
        settings.Set(key, new ContextProviderSettings(true, segmentCadenceMinutes, patterCadenceMinutes, null));
        return settings;
    }

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public static class ScenarioTheWindStormFamilyIsSomber
    {
        [Fact]
        public static void A_tornado_fact_is_filtered()
        {
            // Given a fact containing "tornado" with no casualty words
            // When  the tone gate runs
            // Then  the fact is filtered — the gh-#468 sighting can not recur
            Assert.True(HistoryFactHygiene.IsSomber(
                "1974: A tornado tore through the Midwest, flattening entire neighborhoods."));
        }

        [Fact]
        public static void Hurricane_cyclone_typhoon_and_blizzard_are_filtered_including_plurals()
        {
            Assert.True(HistoryFactHygiene.IsSomber("Hurricane Katrina made landfall near New Orleans."));
            Assert.True(HistoryFactHygiene.IsSomber("Two hurricanes formed in the Atlantic within the same week."));
            Assert.True(HistoryFactHygiene.IsSomber("A cyclone made landfall on the eastern coastline."));
            Assert.True(HistoryFactHygiene.IsSomber("Tropical cyclones are tracked closely each storm season."));
            Assert.True(HistoryFactHygiene.IsSomber("A typhoon swept across the Philippines overnight."));
            Assert.True(HistoryFactHygiene.IsSomber("Two typhoons formed in the Pacific within the same week."));
            Assert.True(HistoryFactHygiene.IsSomber("A blizzard buried the region under three feet of snow."));
            Assert.True(HistoryFactHygiene.IsSomber("Back-to-back blizzards closed the interstate for days."));
        }

        [Fact]
        public static void The_match_stays_word_boundary_anchored()
        {
            // "blizzardry" (or any embedding) does not match — the existing posture. "blizzardry"
            // contains "blizzard" as a substring immediately followed by a word character ("...ard" +
            // "ry"), so there is no \b at that point — the anchored \b(?:...)\b group must not fire.
            Assert.False(HistoryFactHygiene.IsSomber(
                "The blizzardry forecast graphic went viral for its retro design."));
        }
    }

    // gh-#479 — T271 gave every wind-storm noun its plural but left the 13 pre-existing disaster
    // nouns singular-only, so "Two earthquakes struck…" passed the gate. Same idiom as the storm
    // family above: each family's plural form proven caught, grouped the same way.
    public static class ScenarioThePreExistingDisasterVocabularyIsSomberIncludingPlurals
    {
        [Fact]
        public static void Earthquake_tsunami_wildfire_avalanche_and_landslide_plurals_are_filtered()
        {
            Assert.True(HistoryFactHygiene.IsSomber("Two earthquakes struck the region within a week."));
            Assert.True(HistoryFactHygiene.IsSomber("Deadly tsunamis followed the offshore quake."));
            Assert.True(HistoryFactHygiene.IsSomber("Wildfires spread rapidly across the dry hillside."));
            Assert.True(HistoryFactHygiene.IsSomber("Avalanches swept down the mountainside after the storm."));
            Assert.True(HistoryFactHygiene.IsSomber("Landslides buried several homes on the hillside."));
        }

        [Fact]
        public static void Mudslide_epidemic_eruption_massacre_and_shipwreck_plurals_are_filtered()
        {
            Assert.True(HistoryFactHygiene.IsSomber("Mudslides closed the mountain highway for days."));
            Assert.True(HistoryFactHygiene.IsSomber("Epidemics swept through the crowded refugee camps."));
            Assert.True(HistoryFactHygiene.IsSomber("Volcanic eruptions forced thousands to evacuate."));
            Assert.True(HistoryFactHygiene.IsSomber("Massacres were reported in the border villages."));
            Assert.True(HistoryFactHygiene.IsSomber("Shipwrecks were discovered off the northern coast."));
        }

        [Fact]
        public static void Famine_genocide_and_plague_plurals_are_filtered()
        {
            Assert.True(HistoryFactHygiene.IsSomber("Famines gripped the region for successive years."));
            Assert.True(HistoryFactHygiene.IsSomber("Genocides were documented by international observers."));
            Assert.True(HistoryFactHygiene.IsSomber("Plagues swept across medieval Europe."));
        }
    }

    public static class ScenarioThePatterLaneRotatesThroughTheAirableList
    {
        [Fact]
        public static async Task Successive_patter_slots_vend_facts_not_yet_aired_today()
        {
            // Given a day with 3 airable facts (one content generation — a fixed FreshUntil, matching
            // HistoryContextProvider's own same-day-cache shape, SPEC F125.4)
            var time = NewTime();
            var freshUntil = time.GetUtcNow().AddHours(24);
            var provider = new FakeContextProvider("history")
            {
                NextResult = () => new ContextContent(["fact A", "fact B", "fact C"], freshUntil),
            };
            var pipeline = new ContextPipeline(
                [provider], EnabledSettings("history", segmentCadenceMinutes: 1440, patterCadenceMinutes: 10),
                time, new CapturingLogger<ContextPipeline>());
            await pipeline.TickAsync(CancellationToken.None); // Populates the cache TryTakeDuePatterFact reads.

            // When three successive patter-cadence slots come due
            var first = pipeline.TryTakeDuePatterFact();
            time.Advance(TimeSpan.FromMinutes(10));
            var second = pipeline.TryTakeDuePatterFact();
            time.Advance(TimeSpan.FromMinutes(10));
            var third = pipeline.TryTakeDuePatterFact();

            // Then each slot vends an unaired fact, in list order — chosen[0] is dead
            Assert.Equal("fact A", first?.Fact);
            Assert.Equal("fact B", second?.Fact);
            Assert.Equal("fact C", third?.Fact);
        }

        [Fact]
        public static void ContextContent_carries_the_ordered_airable_list()
        {
            // The provider stops pre-choosing; the pipeline selects at vend time (F125.2): one ordered
            // fact list, never a separate pre-chosen SegmentFacts string plus a separate pre-chosen
            // PatterFact string.
            var contentType = typeof(ContextContent);
            Assert.Null(contentType.GetProperty("SegmentFacts"));
            Assert.Null(contentType.GetProperty("PatterFact"));

            var factsProperty = contentType.GetProperty("Facts");
            Assert.NotNull(factsProperty);
            Assert.Equal(typeof(IReadOnlyList<string>), factsProperty.PropertyType);

            var content = new ContextContent(["first", "second", "third"], DateTimeOffset.UtcNow.AddHours(1));
            Assert.Equal(["first", "second", "third"], content.Facts);
        }
    }

    // Review finding F2: a one-element list degenerates DIFFERENTLY per lane, not identically — the
    // segment window always covers the one fact, every vend, but the patter aired-set marks that one
    // index aired on its FIRST vend and holds it for the rest of the content generation, so a second
    // patter-cadence slot inside the SAME generation finds nothing left to vend and skips. Pinned here
    // because ContextContent's and WeatherContextProvider's own doc blocks previously claimed the
    // one-element case behaved identically to pre-F125 (proven false: pre-F125 a single-fact
    // provider's patter line repeated every slot, forever).
    public static class ScenarioASingleFactProviderPattersOncePerGeneration
    {
        [Fact]
        public static async Task Second_patter_slot_in_the_same_generation_skips()
        {
            var time = NewTime();
            var freshUntil = time.GetUtcNow().AddHours(24); // One content generation for the whole test.
            var provider = new FakeContextProvider("weather")
            {
                NextResult = () => new ContextContent(["Calgary: overcast, 23°C."], freshUntil),
            };
            var pipeline = new ContextPipeline(
                [provider], EnabledSettings("weather", segmentCadenceMinutes: 1440, patterCadenceMinutes: 10),
                time, new CapturingLogger<ContextPipeline>());
            await pipeline.TickAsync(CancellationToken.None); // Populates the cache TryTakeDuePatterFact reads.

            var first = pipeline.TryTakeDuePatterFact();
            time.Advance(TimeSpan.FromMinutes(10)); // A second patter-cadence slot, same generation.
            var second = pipeline.TryTakeDuePatterFact();

            Assert.Equal("Calgary: overcast, 23°C.", first?.Fact);
            // Never a repeat (F125.3's own patter rule) — the one fact already aired this generation.
            Assert.Null(second);
        }
    }

    public static class ScenarioTheSegmentLaneRotatesItsWindow
    {
        [Fact]
        public static async Task Successive_segment_slots_advance_the_window_through_the_list()
        {
            // Given successive segment slots in one day, with more airable facts (6) than the 4-fact
            // window
            var time = NewTime();
            var freshUntil = time.GetUtcNow().AddHours(24);
            var provider = new FakeContextProvider("history")
            {
                NextResult = () => new ContextContent(["f0", "f1", "f2", "f3", "f4", "f5"], freshUntil),
            };
            var pipeline = new ContextPipeline(
                [provider], EnabledSettings("history", segmentCadenceMinutes: 10, patterCadenceMinutes: 1440),
                time, new CapturingLogger<ContextPipeline>());

            var firstTick = await pipeline.TickAsync(CancellationToken.None);
            time.Advance(TimeSpan.FromMinutes(10));
            var secondTick = await pipeline.TickAsync(CancellationToken.None);
            time.Advance(TimeSpan.FromMinutes(10));
            var thirdTick = await pipeline.TickAsync(CancellationToken.None);

            // Then the 4-fact window advances rather than repeating the first four
            Assert.Equal("f0 · f1 · f2 · f3", SingleHistorySegment(firstTick).Content.SegmentFacts);
            Assert.Equal("f4 · f5 · f0 · f1", SingleHistorySegment(secondTick).Content.SegmentFacts);
            Assert.Equal("f2 · f3 · f4 · f5", SingleHistorySegment(thirdTick).Content.SegmentFacts);
        }

        static DueContextSegment SingleHistorySegment(IReadOnlyList<DueContextSegment> due) =>
            Assert.Single(due, d => d.Key == "history");
    }

    public static class ScenarioRotationIsObservable
    {
        [Fact]
        public static async Task The_vend_log_line_names_the_chosen_fact_index_and_the_aired_set_size()
        {
            var time = NewTime();
            var freshUntil = time.GetUtcNow().AddHours(24);
            var provider = new FakeContextProvider("history")
            {
                NextResult = () => new ContextContent(["fact A", "fact B"], freshUntil),
            };
            var logger = new CapturingLogger<ContextPipeline>();
            var pipeline = new ContextPipeline(
                [provider], EnabledSettings("history", segmentCadenceMinutes: 1440, patterCadenceMinutes: 10),
                time, logger);

            await pipeline.TickAsync(CancellationToken.None); // Vends a segment window (index 0, aired-set 1 of 2).
            var fact = pipeline.TryTakeDuePatterFact(); // Vends a patter fact (index 0, aired-set 1 of 2).

            Assert.NotNull(fact);
            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Information
                    && entry.Message.Contains("vended segment facts starting at index 0", StringComparison.Ordinal)
                    && entry.Message.Contains("aired-set size 2 of 2", StringComparison.Ordinal));
            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Information
                    && entry.Message.Contains("vended patter fact index 0", StringComparison.Ordinal)
                    && entry.Message.Contains("aired-set size 1 of 2", StringComparison.Ordinal));
        }
    }

    // Review finding O1: a provider is free to keep the SAME FreshUntil across a fetch whose airable
    // list nonetheless changed size (e.g. the tone gate removing a different count of facts on a
    // re-fetch that reused the day's own cached FreshUntil). A facts-COUNT change is therefore its own
    // reset trigger, alongside the FreshUntil roll — otherwise the aired-set could keep indices from
    // the OLD, longer list around, and its logged size could exceed the CURRENT list's own total.
    public static class ScenarioAShrunkListResetsRotationRatherThanMisreportingItsAiredSetSize
    {
        [Fact]
        public static async Task A_facts_count_change_resets_rotation_even_when_FreshUntil_is_unchanged()
        {
            var time = NewTime();
            var freshUntil = time.GetUtcNow().AddHours(24); // Deliberately the SAME across both fetches.
            var provider = new FakeContextProvider("history")
            {
                NextResult = () => new ContextContent(["f0", "f1", "f2", "f3", "f4", "f5"], freshUntil),
            };
            var logger = new CapturingLogger<ContextPipeline>();
            var pipeline = new ContextPipeline(
                [provider], EnabledSettings("history", segmentCadenceMinutes: 10, patterCadenceMinutes: 1440),
                time, logger);

            await pipeline.TickAsync(CancellationToken.None); // Window over the 6-fact list; aired-set grows to 4.

            // The SAME generation's FreshUntil, but the airable list shrinks to 2 on the next fetch.
            provider.NextResult = () => new ContextContent(["g0", "g1"], freshUntil);
            time.Advance(TimeSpan.FromMinutes(10)); // A new segment-cadence slot — triggers the re-fetch.
            var due = await pipeline.TickAsync(CancellationToken.None);

            var segment = Assert.Single(due, d => d.Key == "history");
            Assert.Equal("g0 · g1", segment.Content.SegmentFacts); // The shrunk list, not stale indices.
            Assert.Contains(
                logger.Entries,
                entry => entry.Message.Contains("vended segment facts starting at index 0", StringComparison.Ordinal)
                    // Never "4 of 2" (indices left over from the 6-fact list) — the shape change reset
                    // the aired-set, so it is bounded by the CURRENT list's own total.
                    && entry.Message.Contains("aired-set size 2 of 2", StringComparison.Ordinal));
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public static class ScenarioAnExhaustedPatterDaySkipsNeverRepeats
    {
        [Fact]
        public static async Task When_every_airable_fact_has_aired_the_patter_slot_is_skipped()
        {
            var time = NewTime();
            var freshUntil = time.GetUtcNow().AddHours(24);
            var provider = new FakeContextProvider("history")
            {
                NextResult = () => new ContextContent(["fact A", "fact B"], freshUntil),
            };
            var pipeline = new ContextPipeline(
                [provider], EnabledSettings("history", segmentCadenceMinutes: 1440, patterCadenceMinutes: 10),
                time, new CapturingLogger<ContextPipeline>());
            await pipeline.TickAsync(CancellationToken.None);

            var first = pipeline.TryTakeDuePatterFact();
            time.Advance(TimeSpan.FromMinutes(10));
            var second = pipeline.TryTakeDuePatterFact();
            time.Advance(TimeSpan.FromMinutes(10));
            var third = pipeline.TryTakeDuePatterFact(); // Both facts have now aired once each.

            Assert.Equal("fact A", first?.Fact);
            Assert.Equal("fact B", second?.Fact);
            // Patter is optional color; a repeat is the exact complaint — skip, never repeat.
            Assert.Null(third);
        }
    }

    public static class ScenarioAnExhaustedSegmentDayWraps
    {
        [Fact]
        public static async Task When_the_window_has_consumed_the_list_it_wraps()
        {
            // 5 facts against a 4-fact window: the cursor takes exactly 5 vends to complete one full
            // cycle and land back where it started — the 6th vend must repeat the 1st, verbatim.
            var time = NewTime();
            var freshUntil = time.GetUtcNow().AddHours(24);
            var provider = new FakeContextProvider("history")
            {
                NextResult = () => new ContextContent(["A", "B", "C", "D", "E"], freshUntil),
            };
            var pipeline = new ContextPipeline(
                [provider], EnabledSettings("history", segmentCadenceMinutes: 10, patterCadenceMinutes: 1440),
                time, new CapturingLogger<ContextPipeline>());

            var joins = new List<string>();
            for (var i = 0; i < 6; i++)
            {
                var due = await pipeline.TickAsync(CancellationToken.None);
                joins.Add(Assert.Single(due, d => d.Key == "history").Content.SegmentFacts);
                time.Advance(TimeSpan.FromMinutes(10));
            }

            Assert.Equal("A · B · C · D", joins[0]);
            Assert.Equal("E · A · B · C", joins[1]);
            Assert.Equal("D · E · A · B", joins[2]);
            Assert.Equal("C · D · E · A", joins[3]);
            Assert.Equal("B · C · D · E", joins[4]);
            // A segment repeat hours later beats starving the lane — the wrap, not an exhausted skip.
            Assert.Equal(joins[0], joins[5]);
        }
    }

    public static class ScenarioARestartForgetsGracefully
    {
        [Fact]
        public static async Task A_fresh_aired_set_restarts_rotation_from_the_top()
        {
            // In-memory, day-scoped by ruling (F125.4) — a restart is a brand new ContextPipeline
            // instance (ProviderState is never persisted), which must not "remember" where a previous
            // instance's rotation left off.
            var time = NewTime();
            var freshUntil = time.GetUtcNow().AddHours(24);
            var settings = EnabledSettings("history", segmentCadenceMinutes: 1440, patterCadenceMinutes: 10);
            ContextContent FetchSameFacts() => new(["fact A", "fact B", "fact C"], freshUntil);

            var firstProvider = new FakeContextProvider("history") { NextResult = FetchSameFacts };
            var firstPipeline = new ContextPipeline(
                [firstProvider], settings, time, new CapturingLogger<ContextPipeline>());
            await firstPipeline.TickAsync(CancellationToken.None);
            Assert.Equal("fact A", firstPipeline.TryTakeDuePatterFact()?.Fact);
            time.Advance(TimeSpan.FromMinutes(10));
            Assert.Equal("fact B", firstPipeline.TryTakeDuePatterFact()?.Fact); // Rotation is mid-cycle.

            // "Restart": a fresh ContextPipeline, same provider shape — nothing carries over.
            var secondProvider = new FakeContextProvider("history") { NextResult = FetchSameFacts };
            var secondPipeline = new ContextPipeline(
                [secondProvider], settings, time, new CapturingLogger<ContextPipeline>());
            await secondPipeline.TickAsync(CancellationToken.None);

            Assert.Equal("fact A", secondPipeline.TryTakeDuePatterFact()?.Fact);
        }
    }
}
