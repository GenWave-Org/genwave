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

using GenWave.Core;
using GenWave.Core.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GenWave.Host.Tests.Specs;

public static class FeatureOneVersion
{
    const string PendingAbout = "pending: T589 — About + spectator About inject IAppVersion (STORY-483)";
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
    }

    public sealed class ScenarioAboutShowsTheDisplayForm
    {
        // Given: host IAppVersion from "5.13.2+abc1234"; GET /api/about

        /// <summary>AC5 — version is "v5.13.2"</summary>
        [Fact(Skip = PendingAbout)]
        public void ReadsV5132() => Assert.Fail(PendingAbout);
    }

    public sealed class ScenarioSpectatorAboutShowsTheDisplayForm
    {
        // Given: host IAppVersion from "5.13.2+abc1234"; GET /spectator/api/about

        /// <summary>AC6 — version is "v5.13.2"</summary>
        [Fact(Skip = PendingAbout)]
        public void ReadsV5132() => Assert.Fail(PendingAbout);
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
