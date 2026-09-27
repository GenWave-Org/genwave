// STORY-483 — One version, read once, shown one way (gh-#9 + gh-#868 · SPEC F211.1–F211.2 · PLAN T588,
// T589, T590)
//
// BDD specification — xUnit. T588's own fact proves the composition root's registration reaches DI —
// no database needed (the Gh008/Story404/Story474-AC6 DB-less factory precedent): the bogus Library
// connection string is never touched merely by resolving a singleton. Every other fact here is RED at
// plan time: [Fact(Skip = Pending)] with a loud body — remove the Skip only in the task that makes it
// green. Each Given comment names the arrange the scenario needs.
// Entry point: GET /api/about + /spectator/api/about through WebApplicationFactory with IAppVersion
// replaced by one built from "5.13.2+abc1234"; the UA facts read each fetcher's outgoing request via a
// capturing HttpMessageHandler.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GenWave.Core;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GenWave.Host.Tests.Specs;

public static class FeatureOneVersion
{
    const string PendingUa = "pending: T590 — User-Agent builders inject IAppVersion (STORY-483)";

    public sealed class ScenarioDiResolvesTheOneProvider : IDisposable
    {
        // Given: the real Program.cs composition root, booted with no database reachable

        readonly OneVersionWebFactory factory;
        readonly IAppVersion version;

        public ScenarioDiResolvesTheOneProvider()
        {
            factory = new OneVersionWebFactory();
            version = factory.Services.GetRequiredService<IAppVersion>();
        }

        public void Dispose() => factory.Dispose();

        /// <summary>PLAN T588 — the composition root's AddSingleton&lt;IAppVersion&gt; resolves to the
        /// one AppVersion.</summary>
        [Fact]
        public void ResolvesFromDi() => Assert.IsType<AppVersion>(version);

        /// <summary>PLAN T589 — DI's Display matches an independent read of the SAME Host assembly's
        /// build stamp (the real-stamp oracle: re-derived from the assembly itself, not echoed back
        /// off the same DI instance a second time).</summary>
        [Fact]
        public void MatchesTheHostAssemblyStamp() =>
            Assert.Equal(AppVersion.FromAssembly(typeof(Program).Assembly).Display, version.Display);
    }

    public sealed class ScenarioAboutShowsTheDisplayForm : IAsyncLifetime
    {
        // Given: host IAppVersion from "5.13.2+abc1234"; GET /api/about

        string version = "";

        public async Task InitializeAsync()
        {
            await using var factory = new OneVersionAboutWebFactory();
            var client = await OneVersionAboutWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/about");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            version = body.RootElement.GetProperty("version").GetString() ?? "";
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC5 — version is "v5.13.2"</summary>
        [Fact]
        public void ReadsV5132() => Assert.Equal("v5.13.2", version);
    }

    public sealed class ScenarioSpectatorAboutShowsTheDisplayForm : IAsyncLifetime
    {
        // Given: host IAppVersion from "5.13.2+abc1234"; GET /spectator/api/about

        string version = "";

        public async Task InitializeAsync()
        {
            await using var factory = new OneVersionSpectatorAboutWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/spectator/api/about");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            version = body.RootElement.GetProperty("version").GetString() ?? "";
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC6 — version is "v5.13.2"</summary>
        [Fact]
        public void ReadsV5132() => Assert.Equal("v5.13.2", version);
    }

    public sealed class ScenarioTheUserAgentCarriesTheDisplayForm
    {
        // Given: host IAppVersion from "5.13.2+abc1234"; one fetch each through the MusicBrainz year
        //        lookup, the history context provider and CatalogHttpFetcher, each on a capturing
        //        handler

        const string Expected = "GenWave/v5.13.2 (+https://github.com/GenWave-Org/genwave)";

        /// <summary>AC8 — MusicBrainz UA is <see cref="Expected"/></summary>
        [Fact(Skip = PendingUa)]
        public void MusicBrainz() => Assert.Fail(PendingUa);

        /// <summary>AC8 — history UA is <see cref="Expected"/></summary>
        [Fact(Skip = PendingUa)]
        public void History() => Assert.Fail(PendingUa);

        /// <summary>AC8 — catalog UA is <see cref="Expected"/></summary>
        [Fact(Skip = PendingUa)]
        public void Catalog() => Assert.Fail(PendingUa);
    }
}

/// <summary>
/// <see cref="ScenarioDiResolvesTheOneProvider"/>'s own DB-less factory (the Gh008/Story404/
/// Story474-AC6 precedent) — a bogus <c>ConnectionStrings:Library</c>, never actually reached: nothing
/// this scenario does touches a store, only the DI container itself. Not <c>file</c>-scoped (unlike most
/// of this suite's single-scenario factories) because <see cref="ScenarioDiResolvesTheOneProvider"/>
/// holds it in an instance field across its ctor/Dispose — a <c>file</c>-local type cannot appear in a
/// member signature of the enclosing (non-file-local) scenario type (CS9051; Story479's
/// LiveChoiceListsWebFactory is the same non-file precedent for the same reason).
/// </summary>
sealed class OneVersionWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-t588-one-version");
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
    }
}

/// <summary>
/// AC5's own factory (STORY-483, PLAN T589) — swaps <see cref="IAppVersion"/> for one built from
/// "5.13.2+abc1234" plus DB-less pack-store/admin-query doubles (mirroring STORY-474's own
/// DB-less AC6 factory precedent, that story's own file), so GET /api/about's own
/// <see cref="AttributionProjector"/>/<see cref="IAdminMediaQuery"/> reads never touch a database —
/// this fact's only concern is the version field's display form, not the attribution/library-count
/// projections STORY-474 already owns.
/// </summary>
file sealed class OneVersionAboutWebFactory : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-t589-one-version-about";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IAppVersion>();
            services.AddSingleton<IAppVersion>(AppVersion.From("5.13.2+abc1234"));
            services.RemoveAll<IJinglePackStore>();
            services.AddSingleton<IJinglePackStore>(new FakeJinglePackStore());
            services.RemoveAll<IFontPackStore>();
            services.AddSingleton<IFontPackStore>(new FakeFontPackStore());
            services.RemoveAll<IVoicePackStore>();
            services.AddSingleton<IVoicePackStore>(new FakeVoicePackStore());
            services.RemoveAll<IAdminMediaQuery>();
            services.AddSingleton<IAdminMediaQuery>(new NoOpAdminMediaQuery());
        });
    }

    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
        {
            throw new InvalidOperationException(
                $"OneVersionAboutWebFactory login failed: {(int)login.StatusCode} {login.StatusCode}.");
        }

        return client;
    }
}

/// <summary>
/// AC6's own factory (STORY-483, PLAN T589) — mirrors STORY-170's own DB-less
/// <c>SpectatorAboutWebFactory</c> (that story's own file), with <see cref="IAppVersion"/>
/// additionally swapped for one built from "5.13.2+abc1234"; <see cref="SpectatorController.GetAbout"/>
/// itself reads only the station options and the app version, so the media-catalog/persona doubles
/// below are wired defensively (that precedent's own reasoning), never exercised by this fact.
/// </summary>
file sealed class OneVersionSpectatorAboutWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-t589-one-version-spectator");
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IAppVersion>();
            services.AddSingleton<IAppVersion>(AppVersion.From("5.13.2+abc1234"));
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        });
    }
}

/// <summary>Unused by STORY-483's version-display facts (AC5) — <see cref="AboutController.Get"/>'s
/// only read is <see cref="IAdminMediaQuery.GetReadyMusicCountAsync"/>, which this interface
/// default-implements to 0 (see that member's own remarks); every other member is unreachable here
/// and throws if that ever changes (the Story220_CatalogMoodsBrowse.cs <c>ThrowingAdminLookup</c>
/// idiom).</summary>
file sealed class NoOpAdminMediaQuery : IAdminMediaQuery
{
    public Task<PagedResult<AdminMediaDto>> ListAdminAsync(LibraryScope scope, MediaQuery query, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by STORY-483's version-display facts.");
}
