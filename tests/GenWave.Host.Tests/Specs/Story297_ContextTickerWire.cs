// STORY-297 — Context segments air at boundaries: the Host wire (F107.3, T226)
//
// BDD specification — xUnit. Four facts: (1) Program.cs's own composition root registers exactly
// one ContextTickerService among its IHostedServices — the "one wall-clock actor" claim; (2) the
// four Context:{Key}:* keys per provider, Station:Location:*, and Station:Imaging:* are all Live on
// the settings allowlist; (3)/(4) a DISCRIMINATION PAIR (F1 fix, T226 review) driving the real
// production ContextTickerService/ContextPipeline/provider graph through one tick: every
// Context:*:Enabled at its default (false) makes ZERO outbound HTTP calls and enqueues ZERO
// deferrals (3), while Context:Weather:Enabled=true plus valid Station:Location coordinates makes
// EXACTLY ONE outbound call and enqueues EXACTLY ONE Context deferral (4) — the positive control
// that proves (3) actually discriminates on the Enabled flag rather than being true either way. A
// fact this shape needs BOTH halves: a "disabled ⇒ zero" spec alone cannot tell "correctly gated"
// apart from "wired wrong in a way that always no-ops" — before the F1 fix, swapping the WHOLE
// IHttpClientFactory for the test double silently dropped every typed client's own BaseAddress,
// which made every provider's HTTP call throw before ever reaching the fake handler regardless of
// the Enabled flag; Assert.Empty(Handler.Requests) passed either way, catching nothing.
//
// Every real hosted service besides the one under test (Playout, BoothLog, ThemeCatalogOwnerLoad,
// InstalledFontCatalogLoad, DependencyHealthProbe, ListenerStatsPoller, request-parsing) needs a
// live Postgres/Liquidsoap/Icecast/Kokoro/Ollama to start cleanly — none of which this non-
// Integration suite can reach, and several of which would themselves dial HTTP within the
// discrimination pair's short window, polluting the outbound-call counts with noise unrelated to
// the context seam. Both web factories below therefore capture-or-remove the full IHostedService set
// (mirrors Story125/188's own "fake the external edges, boot the real graph" idiom) — see each
// factory's own remarks for exactly what it keeps real.

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Context.History;
using GenWave.Context.Weather;
using GenWave.Core.Abstractions;
using GenWave.Host.Configuration;
using GenWave.Host.Playout;
using GenWave.Host.Tests.Fakes;
using GenWave.Orchestration;

namespace GenWave.Host.Tests.Specs;

// ── In-process fixtures ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Boots the real Program.cs composition root purely to CAPTURE its own, untouched
/// <see cref="IHostedService"/> registration set — the fact
/// <c>ContextTickerServiceRegistersAsAHostedService</c> proves. Captured BEFORE
/// <c>RemoveAll&lt;IHostedService&gt;()</c> runs, inside the same <c>ConfigureTestServices</c>
/// callback, so the list reflects exactly what Program.cs's own <c>AddGenWaveContextHost</c> (and
/// every other <c>AddHostedService</c> call) registered — nothing this test infrastructure adds
/// back. Every hosted service is then removed (none can start against this suite's fake stack) and
/// <see cref="IMediaCatalog"/>/<see cref="IActivePersonaAccessor"/> are faked so the factory still
/// boots cleanly enough to have produced that capture — mirrors Story125's <c>LlmStatusWebFactory</c>
/// shape one step further (nothing here is even started, only inspected).
/// </summary>
file sealed class ContextTickerRegistrationWebFactory : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-x7z";

    internal List<ServiceDescriptor> HostedServiceDescriptors { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint so
        // ValidateOnStart() is satisfied without injecting them manually (mirrors Story125/188).
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureTestServices(services =>
        {
            HostedServiceDescriptors.AddRange(services.Where(sd => sd.ServiceType == typeof(IHostedService)));

            services.RemoveAll<IHostedService>();

            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        });
    }
}

/// <summary>
/// Boots the real Program.cs composition root with the REAL <see cref="IHttpClientFactory"/> graph
/// intact and drives the REAL <see cref="ContextTickerService"/>/<c>ContextPipeline</c>/provider
/// graph through exactly one tick — the discrimination pair
/// <c>WithEverythingDisabledTheTickerMakesNoOutboundCalls</c>/
/// <c>WithWeatherEnabledAndValidCoordsTheTickerFiresExactlyOnce</c> below proves.
///
/// <para>
/// <b>Transport swap, per typed client — never the whole factory (F1 fix, T226 review).</b> This
/// factory used to <c>RemoveAll&lt;IHttpClientFactory&gt;()</c> and replace it wholesale with
/// <see cref="SingleHandlerHttpClientFactory"/>: that fake's <c>CreateClient</c> never runs
/// <c>GenWave.Context.ContextServiceCollectionExtensions</c>'s own <c>AddHttpClient&lt;T&gt;(client
/// =&gt; ...)</c> configuration action (which sets <see cref="HttpClient.BaseAddress"/>), because
/// that action is applied INSIDE the real <see cref="System.Net.Http.IHttpClientFactory"/>
/// implementation, not by anything inherent to typed-client registration. With no
/// <see cref="HttpClient.BaseAddress"/>, <c>WeatherContextProvider</c>/<c>HistoryContextProvider</c>'s
/// relative-URI <c>HttpClient.GetAsync</c>/<c>SendAsync</c> call throws
/// <see cref="InvalidOperationException"/> BEFORE <see cref="HttpMessageHandler.SendAsync"/> is ever
/// reached — each provider's own broad <c>catch (Exception)</c> swallows it — so
/// <c>Assert.Empty(Handler.Requests)</c> was true whether the provider was enabled or disabled: a
/// vacuous spec, mutation-proven. The fix keeps the REAL <see cref="IHttpClientFactory"/> (so
/// BaseAddress/Timeout/MaxResponseContentBufferSize survive) and swaps only the innermost transport:
/// <c>services.AddHttpClient&lt;T&gt;()</c> called again for a typed client
/// <c>ContextServiceCollectionExtensions</c> already registered composes onto that SAME named
/// client's configuration (both calls resolve to <c>typeof(T).Name</c>) rather than replacing it,
/// so <see cref="Microsoft.Extensions.DependencyInjection.HttpClientBuilderExtensions.ConfigurePrimaryHttpMessageHandler(Microsoft.Extensions.DependencyInjection.IHttpClientBuilder, Func{HttpMessageHandler})"/>
/// only ever swaps what actually sends the request.
/// </para>
///
/// <para>
/// Every OTHER hosted service is removed and <see cref="ContextTickerService"/> is re-registered
/// alone — a legitimate isolation technique here (unlike the registration proof above, this pair is
/// about BEHAVIOR, not about what Program.cs's own registration list contains) that keeps the other
/// services' own real HTTP dialing (dependency health probes chief among them) from polluting the
/// outbound-call counts with noise unrelated to the context seam.
/// </para>
/// </summary>
file sealed class ContextTickerFixtureWebFactory(bool weatherEnabled, bool clockAnchoredIdents = false)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-x7z";

    /// <summary>Records every request it sees; responds with a well-formed Open-Meteo payload so the
    /// positive-control fact's enabled provider actually produces content to enqueue (the disabled
    /// fact never reaches this responder at all — zero requests, this or otherwise).</summary>
    internal FakeHttpMessageHandler Handler { get; } = new((_, _) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"current":{"temperature_2m":22.9,"weather_code":3,"wind_speed_10m":10},"daily":{"temperature_2m_max":[25.1],"temperature_2m_min":[12.1]}}"""),
        }));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Context:Weather:Enabled", weatherEnabled ? "true" : "false");
        builder.UseSetting("Station:Imaging:ClockAnchoredIdents", clockAnchoredIdents ? "true" : "false");

        if (weatherEnabled)
        {
            // Valid coordinates (SPEC F108.1) — WeatherContextProvider.IsAvailable fails closed on a
            // blank/invalid Station:Location, which the disabled-provider half of the pair never
            // needs (Development's own appsettings carries none).
            builder.UseSetting("Station:Location:Latitude", "51.05");
            builder.UseSetting("Station:Location:Longitude", "-114.07");
            builder.UseSetting("Station:Location:SpokenName", "Calgary");
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddHostedService<ContextTickerService>();

            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());

            // F1 fix (T226 review) — see this class's own remarks: swap ONLY the transport per typed
            // client, never the whole IHttpClientFactory.
            services.AddHttpClient<WeatherContextProvider>().ConfigurePrimaryHttpMessageHandler(() => Handler);
            services.AddHttpClient<HistoryContextProvider>().ConfigurePrimaryHttpMessageHandler(() => Handler);
        });
    }
}

// ── Specs ────────────────────────────────────────────────────────────────────────────────────────

public static class FeatureContextTickerWire
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the deployed entry point (composition root + settings surface)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheTickerIsWired
    {
        [Fact]
        public async Task ContextTickerServiceRegistersAsAHostedService()
        {
            await using var factory = new ContextTickerRegistrationWebFactory();

            // Touching Services is what actually triggers ConfigureWebHost's ConfigureTestServices
            // callback (and the capture inside it) to run.
            _ = factory.Services;

            Assert.Single(
                factory.HostedServiceDescriptors,
                sd => sd.ImplementationType == typeof(ContextTickerService));
        }

        [Fact]
        public void ContextSettingsAreAllowlisted()
        {
            string[] expectedKeys =
            [
                "Context:Weather:Enabled",
                "Context:Weather:SegmentCadenceMinutes",
                "Context:Weather:PatterCadenceMinutes",
                "Context:Weather:PersonaId",
                "Context:History:Enabled",
                "Context:History:SegmentCadenceMinutes",
                "Context:History:PatterCadenceMinutes",
                "Context:History:PersonaId",
                "Station:Location:Latitude",
                "Station:Location:Longitude",
                "Station:Location:SpokenName",
                "Station:Imaging:ClockAnchoredIdents",
                "Station:Imaging:TimeAnnouncements",
            ];

            foreach (var key in expectedKeys)
            {
                Assert.True(
                    StationSettingsAllowlist.ByKey.TryGetValue(key, out var setting),
                    $"'{key}' is not on the station settings allowlist.");
                Assert.Equal(SettingApplyMode.Live, setting.ApplyMode);
            }
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — paired with its own positive control (F1 fix, T226 review) so the "disabled ⇒
    // zero" fact below is proven to actually discriminate on Context:Weather:Enabled, not merely
    // true regardless — see FeatureContextTickerWire's own remarks for why the pair, not either
    // fact alone, is what closes the mutation-proven gap.
    // ---------------------------------------------------------------------

    public sealed class ScenarioDisabledMeansSilentAndOffline
    {
        [Fact]
        public async Task WithEverythingDisabledTheTickerMakesNoOutboundCalls()
        {
            await using var factory = new ContextTickerFixtureWebFactory(weatherEnabled: false);

            // The re-registered ContextTickerService instance — see the factory's own remarks.
            // Resolving it constructs the whole chain a real tick needs (ContextPipeline,
            // WeatherContextProvider/HistoryContextProvider, their typed HttpClients) without
            // starting its background timer loop, so TickOnceAsync below is the deterministic,
            // single-tick equivalent of "the ticker ran once" — no real-time wait needed.
            var ticker = Assert.Single(factory.Services.GetServices<IHostedService>().OfType<ContextTickerService>());

            await ticker.TickOnceAsync(CancellationToken.None);

            Assert.Empty(factory.Handler.Requests);

            var deferralQueue = factory.Services.GetRequiredService<SpeechDeferralQueue>();
            Assert.Null(deferralQueue.NextDue);
        }

        /// <summary>
        /// The positive control (F1 fix, T226 review): the SAME chain as
        /// <see cref="WithEverythingDisabledTheTickerMakesNoOutboundCalls"/> above, with
        /// Context:Weather:Enabled=true and valid Station:Location coordinates instead — proves the
        /// disabled fact's "zero" is a real gate, not a vacuously-always-zero wire, by showing the
        /// identical chain produces exactly ONE outbound call and exactly ONE enqueued deferral the
        /// instant the only thing that changed is the Enabled flag (plus the coordinates weather's
        /// own F108.1 fail-closed check requires).
        /// </summary>
        [Fact]
        public async Task WithWeatherEnabledAndValidCoordsTheTickerFiresExactlyOnce()
        {
            await using var factory = new ContextTickerFixtureWebFactory(weatherEnabled: true);

            var ticker = Assert.Single(factory.Services.GetServices<IHostedService>().OfType<ContextTickerService>());

            await ticker.TickOnceAsync(CancellationToken.None);

            Assert.Single(factory.Handler.Requests);

            var deferralQueue = factory.Services.GetRequiredService<SpeechDeferralQueue>();
            Assert.NotNull(deferralQueue.NextDue);

            var due = deferralQueue.PeekNextDue();
            Assert.NotNull(due);
            Assert.Equal(SpeechDeferralKind.Context, due.Kind);
            Assert.Equal("weather", due.Discriminator);
        }
    }

    // ---------------------------------------------------------------------
    // The T230 rider: the SAME ticker also drives ClockAnchoredImagingProducer each tick — proven
    // through the real composition root, not merely a unit-level producer.Produce() call (that
    // coverage already lives in GenWave.Orchestration.Tests/Specs/Story301_ClockAnchoredIdents.cs).
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheTickerAlsoWiresTheImagingProducer
    {
        [Fact]
        public async Task ClockAnchoredIdentsOnEnqueuesAStationIdDeferralOnTheSameTick()
        {
            await using var factory = new ContextTickerFixtureWebFactory(weatherEnabled: false, clockAnchoredIdents: true);

            var ticker = Assert.Single(factory.Services.GetServices<IHostedService>().OfType<ContextTickerService>());

            await ticker.TickOnceAsync(CancellationToken.None);

            // No context provider is enabled in this fixture — zero outbound calls either way; the
            // deferral below comes from ClockAnchoredImagingProducer, not ContextPipeline.
            Assert.Empty(factory.Handler.Requests);

            var deferralQueue = factory.Services.GetRequiredService<SpeechDeferralQueue>();
            var due = deferralQueue.PeekNextDue();
            Assert.NotNull(due);
            Assert.Equal(SpeechDeferralKind.StationId, due.Kind);
        }
    }
}
