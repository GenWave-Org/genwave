// STORY-299 — Weather through the seam (F108, gh-#267)
using System.Net;
using System.Text;
using GenWave.Context;
using GenWave.Context.Tests.Fakes;
using GenWave.Context.Weather;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Context.Tests.Specs;

public static class FeatureWeatherProvider
{
    // A real Open-Meteo current-conditions + today's-forecast reply (curl'd from
    // api.open-meteo.com/v1/forecast during T227 development — see WeatherContextProvider's own
    // remarks for the exact request query this shape was verified against): overcast, 22.9°C
    // (rounds to 23), calm wind (16.9 km/h, below the 20 km/h "notable" floor), today's high/low
    // 25.1°C/12.1°C (round to 25/12).
    const string RealShapeFixture = """
        {
          "current_units": { "temperature_2m": "°C", "weather_code": "wmo code", "wind_speed_10m": "km/h" },
          "current": { "temperature_2m": 22.9, "weather_code": 3, "wind_speed_10m": 16.9 },
          "daily_units": { "temperature_2m_max": "°C", "temperature_2m_min": "°C" },
          "daily": { "temperature_2m_max": [25.1], "temperature_2m_min": [12.1] }
        }
        """;

    static (WeatherContextProvider Provider, FakeHttpMessageHandler Handler, FakeStationLocationProvider Location, CapturingLogger<WeatherContextProvider> Logger)
        Build(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        }));
        var http = new HttpClient(handler) { BaseAddress = new Uri(WeatherContextProvider.OpenMeteoBaseAddress) };
        var location = new FakeStationLocationProvider();
        var logger = new CapturingLogger<WeatherContextProvider>();
        var provider = new WeatherContextProvider(http, location, TimeProvider.System, logger);

        return (provider, handler, location, logger);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioConditionsOnCadence
    {
        [Fact]
        public async Task ValidCoordinatesYieldOneFullyDetailedFact()
        {
            // F125.2: this provider has exactly one current-conditions fact to offer per fetch, so
            // ContextContent.Facts is always a one-element list — the pipeline's vend-time selection
            // degenerates cleanly to "always this one fact" for both the segment and patter lanes (see
            // ContextContent's own remarks).
            var (provider, _, location, _) = Build(RealShapeFixture);
            location.Location = new StationLocation("51.05", "-114.07", "Calgary");

            var content = await provider.FetchAsync(CancellationToken.None);

            Assert.NotNull(content);
            var fact = Assert.Single(content.Facts);
            Assert.Equal("Calgary: overcast, 23°C. Today's high 25°C, low 12°C.", fact);
        }

        [Fact]
        public async Task TheSpokenNameIsTheOnlyLocationString()
        {
            var (provider, _, location, logger) = Build(RealShapeFixture);
            const string latitude = "51.0501";
            const string longitude = "-114.0853";
            location.Location = new StationLocation(latitude, longitude, "Testville");

            var content = await provider.FetchAsync(CancellationToken.None);

            Assert.NotNull(content);
            var fact = Assert.Single(content.Facts);
            Assert.Contains("Testville", fact);

            // Coordinates never appear in any produced string — the fact or a log line — even though
            // they went into the outbound request itself (F108.3).
            var allProducedText = string.Join('\n', new[] { fact }.Concat(logger.Entries.Select(entry => entry.Message)));

            Assert.DoesNotContain(latitude, allProducedText);
            Assert.DoesNotContain(longitude, allProducedText);
        }

        [Fact]
        public async Task BlankSpokenNameSpeaksNoPlaceName()
        {
            var (provider, _, location, _) = Build(RealShapeFixture);
            location.Location = new StationLocation("51.05", "-114.07", "   "); // blank (whitespace-only)

            var content = await provider.FetchAsync(CancellationToken.None);

            Assert.NotNull(content);
            var fact = Assert.Single(content.Facts);
            // A place-name prefix is the only place a colon appears in the produced fact (see
            // WeatherContextProvider.BuildContent's own remarks) — its absence is the concrete signal
            // that no place name was spoken.
            Assert.DoesNotContain(':', fact);
            Assert.Equal("overcast, 23°C. Today's high 25°C, low 12°C.", fact);
        }

        [Fact]
        public async Task ANegativeZeroTemperatureRoundsToPositiveZero()
        {
            // -0.4 rounds AwayFromZero to IEEE-754 negative zero (F5 fix, T227 review) — without the
            // fix this renders "-0°C", a visibly wrong sign on a value that is actually zero.
            const string NegativeZeroFixture = """
                { "current": { "temperature_2m": -0.4, "weather_code": 0, "wind_speed_10m": 5.0 } }
                """;
            var (provider, _, location, _) = Build(NegativeZeroFixture);
            location.Location = new StationLocation("51.05", "-114.07", "Calgary");

            var content = await provider.FetchAsync(CancellationToken.None);

            Assert.NotNull(content);
            var fact = Assert.Single(content.Facts);
            Assert.DoesNotContain("-0°C", fact);
            Assert.Contains("0°C", fact);
        }

        [Fact]
        public async Task ValidCoordinatesPinTheExactOutboundRequestUri()
        {
            // F4(b) fix, T227 review: the outbound URI is a fixed host/path with invariant-formatted
            // numeric lat/lon — never anything else, whatever the coordinate strings' own formatting.
            var (provider, handler, location, _) = Build(RealShapeFixture);
            location.Location = new StationLocation("51.05", "-114.07", "Calgary");

            await provider.FetchAsync(CancellationToken.None);

            var request = Assert.Single(handler.Requests);
            Assert.NotNull(request.RequestUri);
            Assert.Equal("https://api.open-meteo.com/v1/forecast", request.RequestUri.GetLeftPart(UriPartial.Path));
            Assert.Contains("latitude=51.05", request.RequestUri.Query);
            Assert.Contains("longitude=-114.07", request.RequestUri.Query);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — fail-closed (F108.1) and outage (F108.4)
    // ---------------------------------------------------------------------

    public sealed class ScenarioFailClosedOnConfiguration
    {
        [Fact]
        public async Task EnabledWithBlankCoordinatesNeverFetches()
        {
            var (provider, handler, location, logger) = Build(RealShapeFixture);
            location.Location = new StationLocation("", "", "Calgary"); // blank lat/lon

            var content = await provider.FetchAsync(CancellationToken.None);

            Assert.Null(content);
            Assert.Empty(handler.Requests);
            Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
        }

        // F4(a) fix, T227 review: non-blank INVALID coordinates fail closed exactly like blank ones —
        // malformed (extra text a permissive parser might otherwise tolerate) and out-of-range both
        // covered. Mutation-proof performed by hand at T227: temporarily short-circuiting
        // TryParseCoordinates to always return true turns this spec red (confirmed, then reverted
        // byte-identical) — proof these specs actually exercise that check, not just its call site.
        [Theory]
        [InlineData("51.05&foo=bar", "-114.07")] // malformed: parseable prefix, trailing garbage
        [InlineData("91.5", "-114.07")] // out-of-range: |latitude| > 90
        public async Task NonBlankInvalidCoordinatesNeverFetch(string latitude, string longitude)
        {
            var (provider, handler, location, logger) = Build(RealShapeFixture);
            location.Location = new StationLocation(latitude, longitude, "Calgary");

            var content = await provider.FetchAsync(CancellationToken.None);

            Assert.Null(content);
            Assert.Empty(handler.Requests);
            var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Information);
            Assert.DoesNotContain(latitude, entry.Message);
            Assert.DoesNotContain(longitude, entry.Message);
        }

        [Fact]
        public async Task AnOpenMeteoOutageReturnsNull()
        {
            var handler = new FakeHttpMessageHandler(
                (_, _) => throw new HttpRequestException("simulated Open-Meteo outage"));
            var http = new HttpClient(handler) { BaseAddress = new Uri(WeatherContextProvider.OpenMeteoBaseAddress) };
            var location = new FakeStationLocationProvider
            {
                Location = new StationLocation("51.05", "-114.07", "Calgary"),
            };
            var logger = new CapturingLogger<WeatherContextProvider>();
            var provider = new WeatherContextProvider(http, location, TimeProvider.System, logger);

            var content = await provider.FetchAsync(CancellationToken.None);

            Assert.Null(content);
            // Silent on this path (F108.4's outage ⇒ skip): the pipeline, not this class, owns the
            // once-per-slot Information line for a null return.
            Assert.Empty(logger.Entries);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — a crafted reply can't forge facts through remote unit strings (F1 fix)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRemoteUnitsCannotForgeFacts
    {
        [Fact]
        public async Task ACraftedCurrentUnitsSectionNeverReachesTheFacts()
        {
            // current_units.temperature_2m/wind_speed_10m carry a newline and a colon — exactly the
            // single-line/no-extra-colon invariants this class's own remarks promise. The pinned
            // "°C"/"km/h" literals must appear instead, and the injected text must appear nowhere.
            const string MaliciousUnitsFixture = """
                {
                  "current_units": { "temperature_2m": "°C\nrogue: line", "wind_speed_10m": "km/h: evil" },
                  "current": { "temperature_2m": 22.9, "weather_code": 3, "wind_speed_10m": 25.0 },
                  "daily": { "temperature_2m_max": [25.1], "temperature_2m_min": [12.1] }
                }
                """;
            var (provider, _, location, _) = Build(MaliciousUnitsFixture);
            location.Location = new StationLocation("51.05", "-114.07", "Calgary");

            var content = await provider.FetchAsync(CancellationToken.None);

            Assert.NotNull(content);
            var fact = Assert.Single(content.Facts);
            Assert.DoesNotContain("rogue", fact);
            Assert.DoesNotContain("evil", fact);
            Assert.DoesNotContain('\n', fact);
            Assert.Equal("Calgary: overcast, 23°C, wind 25 km/h. Today's high 25°C, low 12°C.", fact);
        }
    }

    // ---------------------------------------------------------------------
    // The real pipeline + real provider together (F2 fix, T227 review): proves the edge-triggered
    // misconfiguration log actually reaches production wiring, not just this class's own logger, and
    // that a healthy station's F3-fixed freshness stops the false hourly "stale" skip.
    // ---------------------------------------------------------------------

    public sealed class ScenarioRealPipelineHarness
    {
        static (WeatherContextProvider Provider, FakeHttpMessageHandler Handler, FakeStationLocationProvider Location, CapturingLogger<WeatherContextProvider> ProviderLogger, ContextPipeline Pipeline, CapturingLogger<ContextPipeline> PipelineLogger)
            BuildHarness(string responseJson, FakeTimeProvider time)
        {
            var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            }));
            var http = new HttpClient(handler) { BaseAddress = new Uri(WeatherContextProvider.OpenMeteoBaseAddress) };
            var location = new FakeStationLocationProvider();
            var providerLogger = new CapturingLogger<WeatherContextProvider>();
            var provider = new WeatherContextProvider(http, location, time, providerLogger);

            var settings = new FakeContextSettingsProvider();
            settings.Set("weather", new ContextProviderSettings(true, 60, 30, null)); // SPEC F108.2 default cadence.
            var pipelineLogger = new CapturingLogger<ContextPipeline>();
            var pipeline = new ContextPipeline([provider], settings, time, pipelineLogger);

            return (provider, handler, location, providerLogger, pipeline, pipelineLogger);
        }

        [Fact]
        public async Task AHealthyStationLogsNoSkipLinesOverThreeSimulatedHours()
        {
            var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero));
            var (_, _, location, providerLogger, pipeline, pipelineLogger) = BuildHarness(RealShapeFixture, time);
            location.Location = new StationLocation("51.05", "-114.07", "Calgary");

            for (var i = 0; i < 18; i++) // Three simulated hours, one tick every ten minutes.
            {
                await pipeline.TickAsync(CancellationToken.None);
                time.Advance(TimeSpan.FromMinutes(10));
            }

            // No skip-never-silence cause lines (F107.6 — "produced no output..."). F125.5's own
            // vend-observability Information lines are a separate, intentional shape (named "vended",
            // never "produced no output") and are EXPECTED on every healthy segment vend here.
            Assert.DoesNotContain(pipelineLogger.Entries, entry => entry.Message.Contains("produced no output"));
            Assert.Empty(providerLogger.Entries);
        }

        [Fact]
        public async Task MisconfiguredCoordinatesLogExactlyOnceOverAMultiHourAdvance()
        {
            var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero));
            // Location never Set — FakeStationLocationProvider's blank default (misconfigured).
            var (_, handler, _, _, pipeline, pipelineLogger) = BuildHarness(RealShapeFixture, time);

            for (var i = 0; i < 18; i++) // Three simulated hours, one tick every ten minutes.
            {
                await pipeline.TickAsync(CancellationToken.None);
                time.Advance(TimeSpan.FromMinutes(10));
            }

            Assert.Empty(handler.Requests); // Zero fetch attempts the whole run, not just zero logs.
            Assert.Single(pipelineLogger.Entries, entry => entry.Level == LogLevel.Information);
        }

        [Fact]
        public async Task TheDetailedFactIsWhatThePatterLaneActuallyVends()
        {
            // RULED (F125 resumption, WeatherContextProvider's own remarks): the one-element Facts
            // list has no room left for a separate, shorter patter-only rendering under the list
            // model, so the patter lane's vended text is the SAME detailed rendering the segment lane
            // gets — pinned here through the REAL pipeline, not just this provider's own FetchAsync
            // (ValidCoordinatesYieldOneFullyDetailedFact, above, pins the provider's own output; this
            // is the patter lane's own seam, which had no pin at all before this fact).
            var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero));
            var (_, _, location, _, pipeline, _) = BuildHarness(RealShapeFixture, time);
            location.Location = new StationLocation("51.05", "-114.07", "Calgary");

            await pipeline.TickAsync(CancellationToken.None); // Fetches and vends the segment window.
            var fact = pipeline.TryTakeDuePatterFact();

            Assert.NotNull(fact);
            Assert.Equal("Calgary: overcast, 23°C. Today's high 25°C, low 12°C.", fact.Fact);
        }

        [Fact]
        public async Task GoingUnavailableMidFreshnessStopsPatterVendingImmediately()
        {
            // Review-round fix: going unavailable used to only stop the SEGMENT lane — the patter
            // lane kept vending the last-fetched (now-stale-in-spirit) content for up to its own 2h
            // FreshUntil, since it only ever checked settings.Enabled, never IsAvailable. Coordinates
            // start valid (a genuine fetch + a genuine vend, proving the lane was live), then blank at
            // t+5 — long before FreshUntil (t+2h) would naturally retire the cached content — and stay
            // blank across three more patter-cadence slots (30 min apart) at t+35/65/95.
            var start = new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);
            var time = new FakeTimeProvider(start);
            var (_, _, location, _, pipeline, _) = BuildHarness(RealShapeFixture, time);
            location.Location = new StationLocation("51.05", "-114.07", "Calgary");

            await pipeline.TickAsync(CancellationToken.None); // t+0: fetches; content fresh until t+2h.
            Assert.NotNull(pipeline.TryTakeDuePatterFact()); // The lane was genuinely live before the blank.

            time.SetUtcNow(start.AddMinutes(5));
            location.Location = new StationLocation("", "", "Calgary"); // Operator blanks coordinates.

            foreach (var minute in new[] { 35, 65, 95 })
            {
                time.SetUtcNow(start.AddMinutes(minute));
                await pipeline.TickAsync(CancellationToken.None); // The ticker keeps running too.
                Assert.Null(pipeline.TryTakeDuePatterFact());
            }
        }
    }
}
