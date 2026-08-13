// STORY-296 — The context seam exists: pipeline semantics (F107.2, F107.6)
using GenWave.Context.Tests.Fakes;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Context.Tests.Specs;

public static class FeatureContextPipeline
{
    // A fixed, cadence-aligned instant (an exact UTC midnight — always a multiple of every cadence
    // width this suite uses in minutes) so "how far into its slot is `now`" never depends on the
    // wall clock the test happened to run at.
    static readonly DateTimeOffset StartTime = new(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);

    static FakeTimeProvider NewTime() => new(StartTime);

    static FakeContextSettingsProvider EnabledSettings(
        string key, int segmentCadenceMinutes, int patterCadenceMinutes)
    {
        var settings = new FakeContextSettingsProvider();
        settings.Set(key, new ContextProviderSettings(true, segmentCadenceMinutes, patterCadenceMinutes, null));
        return settings;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioFetchOncePerCadenceSlot
    {
        [Fact]
        public async Task TwoTicksInsideOneSlotFetchAtMostOnce()
        {
            var time = NewTime();
            var provider = new FakeContextProvider("weather")
            {
                NextResult = () => new ContextContent(["facts"], time.GetUtcNow().AddHours(1)),
            };
            var pipeline = new ContextPipeline(
                [provider], EnabledSettings("weather", 60, 60), time, new CapturingLogger<ContextPipeline>());

            await pipeline.TickAsync(CancellationToken.None);
            time.Advance(TimeSpan.FromMinutes(5));
            await pipeline.TickAsync(CancellationToken.None);

            Assert.Equal(1, provider.FetchCount);
        }

        [Fact]
        public async Task ANewSlotFetchesAgain()
        {
            var time = NewTime();
            var provider = new FakeContextProvider("weather")
            {
                NextResult = () => new ContextContent(["facts"], time.GetUtcNow().AddHours(1)),
            };
            var pipeline = new ContextPipeline(
                [provider], EnabledSettings("weather", 60, 60), time, new CapturingLogger<ContextPipeline>());

            await pipeline.TickAsync(CancellationToken.None);
            time.Advance(TimeSpan.FromMinutes(65)); // Past the 60-minute cadence boundary.
            await pipeline.TickAsync(CancellationToken.None);

            Assert.Equal(2, provider.FetchCount);
        }

        [Fact]
        public async Task FreshContentServesWithoutRefetch()
        {
            var time = NewTime();
            var provider = new FakeContextProvider("weather")
            {
                NextResult = () => new ContextContent(["a compact fact"], time.GetUtcNow().AddHours(2)),
            };
            var pipeline = new ContextPipeline(
                [provider], EnabledSettings("weather", 60, 60), time, new CapturingLogger<ContextPipeline>());

            var firstTick = await pipeline.TickAsync(CancellationToken.None);
            time.Advance(TimeSpan.FromMinutes(10)); // Still inside the same 60-minute slot.
            var secondTick = await pipeline.TickAsync(CancellationToken.None);

            Assert.Equal(1, provider.FetchCount);
            Assert.Contains(firstTick, due => due.Key == "weather");
            // Already handed off for this slot on the first tick — the second tick must not enqueue
            // the very same content again, but the underlying cache is still alive: the patter lane
            // (a separate cadence, never yet vended) still resolves it from cache with no refetch.
            Assert.Empty(secondTick);
            Assert.NotNull(pipeline.TryTakeDuePatterFact());
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — skip, never silence (F107.6)
    // ---------------------------------------------------------------------

    public sealed class ScenarioSkipNeverSilence
    {
        [Fact]
        public async Task DisabledProviderProducesNothingAndFetchesNothing()
        {
            var time = NewTime();
            var provider = new FakeContextProvider("weather")
            {
                NextResult = () => new ContextContent(["facts"], time.GetUtcNow().AddHours(1)),
            };
            // Never Set — every key resolves to FakeContextSettingsProvider.Disabled.
            var pipeline = new ContextPipeline(
                [provider], new FakeContextSettingsProvider(), time, new CapturingLogger<ContextPipeline>());

            var due = await pipeline.TickAsync(CancellationToken.None);

            Assert.Equal(0, provider.FetchCount);
            Assert.Empty(due);
            Assert.Null(pipeline.TryTakeDuePatterFact());
        }

        [Fact]
        public async Task DisabledProviderLogsAtMostOnceAcrossAMultiHourAdvance()
        {
            var time = NewTime();
            var provider = new FakeContextProvider("weather")
            {
                NextResult = () => new ContextContent(["facts"], time.GetUtcNow().AddHours(1)),
            };
            // Never Set — every key resolves to FakeContextSettingsProvider.Disabled, whose
            // SegmentCadenceMinutes (0) clamps to a one-minute cadence slot (F7 regression pin): the
            // OLD per-slot "disabled" logging would have produced one Information line per registered
            // provider EVERY MINUTE here — 240 of them over this simulated four hours. Edge-triggered
            // logging (this class's remarks) must produce exactly one, on the very first tick.
            var logger = new CapturingLogger<ContextPipeline>();
            var pipeline = new ContextPipeline([provider], new FakeContextSettingsProvider(), time, logger);

            for (var i = 0; i < 240; i++) // Four simulated hours, one tick per minute.
            {
                await pipeline.TickAsync(CancellationToken.None);
                time.Advance(TimeSpan.FromMinutes(1));
            }

            Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
        }

        [Fact]
        public async Task NullReturnProducesNoOutputAndNoError()
        {
            var time = NewTime();
            var provider = new FakeContextProvider("weather") { NextResult = () => null };
            var logger = new CapturingLogger<ContextPipeline>();
            var pipeline = new ContextPipeline([provider], EnabledSettings("weather", 60, 60), time, logger);

            var due = await pipeline.TickAsync(CancellationToken.None);

            Assert.Empty(due);
            // Null is a contract value ("nothing to say"), never a fault: no Warning/Error line, and
            // exactly one Information line for the whole slot.
            Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
            Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
        }

        [Fact]
        public async Task AThrowingProviderLogsOneInformationLinePerSlot()
        {
            var time = NewTime();
            var provider = new FakeContextProvider("weather")
            {
                NextResult = () => throw new InvalidOperationException("upstream exploded"),
            };
            var logger = new CapturingLogger<ContextPipeline>();
            var pipeline = new ContextPipeline([provider], EnabledSettings("weather", 60, 60), time, logger);

            // Two ticks inside the failed slot ⇒ exactly one Information line naming provider + cause;
            // no retry storm.
            await pipeline.TickAsync(CancellationToken.None);
            time.Advance(TimeSpan.FromMinutes(5));
            await pipeline.TickAsync(CancellationToken.None);

            Assert.Equal(1, provider.FetchCount);
            var informationEntries = logger.Entries.Where(entry => entry.Level == LogLevel.Information).ToList();
            var entry = Assert.Single(informationEntries);
            Assert.Contains("weather", entry.Message);
            // F108.3: the cause names the failure KIND, never echoes the provider's own message text.
            Assert.DoesNotContain("upstream exploded", entry.Message);
        }

        [Fact]
        public async Task AHostileProviderWithNullFactsProducesNoOutputAndNoError()
        {
            // F4 fix, T228 review (still applies post-F125.2): ContextContent.Facts is declared
            // `IReadOnlyList<string>` (never nullable) but the record itself validates nothing at
            // runtime — a broken/hostile third-party provider can still hand back one whose Facts is
            // null despite that compile-time contract. Sanitizing that must degrade to skip-never-
            // silence exactly like a thrown FetchAsync, never escape TickAsync as an uncaught
            // exception. `null!` deliberately violates the contract to prove the guard — never legal
            // production input.
            var time = NewTime();
            var provider = new FakeContextProvider("weather")
            {
                NextResult = () => new ContextContent(null!, time.GetUtcNow().AddHours(1)),
            };
            var logger = new CapturingLogger<ContextPipeline>();
            var pipeline = new ContextPipeline([provider], EnabledSettings("weather", 60, 60), time, logger);

            var due = await pipeline.TickAsync(CancellationToken.None);

            Assert.Empty(due);
            Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
            Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
        }

        [Fact]
        public async Task StaleContentIsNeverServed()
        {
            var time = NewTime();
            // FreshUntil expires well inside the 60-minute cadence slot — no retry is allowed until
            // the NEXT slot (fetch-once-per-slot), so once it goes stale it must simply stop serving.
            var provider = new FakeContextProvider("weather")
            {
                NextResult = () => new ContextContent(["facts"], time.GetUtcNow().AddMinutes(10)),
            };
            var pipeline = new ContextPipeline(
                [provider], EnabledSettings("weather", 60, 60), time, new CapturingLogger<ContextPipeline>());

            var firstTick = await pipeline.TickAsync(CancellationToken.None);
            time.Advance(TimeSpan.FromMinutes(20)); // Past FreshUntil, still inside the same slot.
            var secondTick = await pipeline.TickAsync(CancellationToken.None);

            Assert.Contains(firstTick, due => due.Key == "weather");
            Assert.Empty(secondTick);
            Assert.Equal(1, provider.FetchCount); // No retry within the slot.
            Assert.Null(pipeline.TryTakeDuePatterFact());
        }
    }

    // ---------------------------------------------------------------------
    // The patter lane's seam (SPEC F107.5, STORY-298, PLAN T225): ContextPipeline is THE production
    // IContextPatterFactSource — GenWave.Tts depends on the interface alone (Core), never on this L1
    // project, so this structural fact is what keeps that promise honest.
    // ---------------------------------------------------------------------

    public sealed class ScenarioPatterLaneSeam
    {
        [Fact]
        public void PipelineImplementsThePatterFactSourceContract()
        {
            var pipeline = new ContextPipeline(
                [], new FakeContextSettingsProvider(), NewTime(), new CapturingLogger<ContextPipeline>());

            Assert.IsAssignableFrom<IContextPatterFactSource>(pipeline);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the constructor fails fast on an invalid provider key (F107.1, T221/T222 review)
    // ---------------------------------------------------------------------

    public sealed class ScenarioConstructorFailsFastOnAnInvalidKey
    {
        static void Build(params IContextProvider[] providers) =>
            new ContextPipeline(
                providers, new FakeContextSettingsProvider(), NewTime(), new CapturingLogger<ContextPipeline>());

        [Fact]
        public void ADuplicateKeyThrows()
        {
            Assert.Throws<ArgumentException>(
                () => Build(new FakeContextProvider("weather"), new FakeContextProvider("weather")));
        }

        [Fact]
        public void AnUppercaseKeyThrows()
        {
            Assert.Throws<ArgumentException>(() => Build(new FakeContextProvider("Weather")));
        }

        [Fact]
        public void AKeyContainingASpaceThrows()
        {
            Assert.Throws<ArgumentException>(() => Build(new FakeContextProvider("weather report")));
        }

        [Fact]
        public void AnEmptyKeyThrows()
        {
            Assert.Throws<ArgumentException>(() => Build(new FakeContextProvider("")));
        }

        // The F5 regression pin: .NET regex's `$` anchor matches just before a trailing '\n', not
        // only at the true end of the string, so "^[a-z0-9-]+$" alone would have let this key through.
        // The key contract (\z anchor) must still reject it.
        [Fact]
        public void AKeyWithATrailingNewlineThrows()
        {
            Assert.Throws<ArgumentException>(() => Build(new FakeContextProvider("weather\n")));
        }
    }
}
