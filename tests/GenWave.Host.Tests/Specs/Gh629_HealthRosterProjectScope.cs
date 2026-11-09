// gh-#629 — the Health roster is scoped to the station's own compose project
//
// BDD specification — xUnit. A fake docker daemon (FakeHttpMessageHandler) holds a socket-wide
// container set and honours the `filters` query param the way docker does (label=k=v is exact
// equality), so the specs prove what actually reaches the roster, not just what was requested.
// Source-level scenarios drive DockerContainerStatsSource directly; the endpoint scenario goes
// through the real /api/health/containers pipeline via WebApplicationFactory.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Host.Options;
using GenWave.Host.Stats;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

file sealed class RosterScopeWebFactory(FakeHttpMessageHandler daemon) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-gh629";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("DockerStats:BaseUrl", "http://dockerproxy.test:2375");
        builder.UseSetting("DockerStats:ComposeProject", "genwave");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
            services.AddHttpClient<DockerContainerStatsSource>()
                .ConfigurePrimaryHttpMessageHandler(() => daemon);
        });
    }
}

public static class FeatureHealthRosterProjectScope
{
    const string BaseUrl = "http://proxy.test:2375";
    const string ProjectLabel = "com.docker.compose.project";
    const string ExpectedFilter = """{"label":["com.docker.compose.project=genwave"]}""";

    // The socket-wide container set: the station, a utility helper inside it, a foreign buildx
    // builder, and a fixture container from a look-alike project (the dev-box shape).
    static readonly (string Id, string Name, string? Project, string Service, bool Utility)[] Socket =
    [
        ("aaaaaaaaaaaa1111", "/genwave-api-1", "genwave", "api", false),
        ("bbbbbbbbbbbb2222", "/genwave-ollama-init-1", "genwave", "ollama-init", true),
        ("cccccccccccc3333", "/buildx_buildkit_builder0", null, "", false),
        ("dddddddddddd4444", "/genwave-t434c-2c93c722c5-db-1", "genwave-t434c-2c93c722c5", "t434c-db", false),
    ];

    static string ContainerJson((string Id, string Name, string? Project, string Service, bool Utility) c)
    {
        var labels = new Dictionary<string, string>();
        if (c.Project is not null)
        {
            labels[ProjectLabel] = c.Project;
            labels["com.docker.compose.service"] = c.Service;
        }
        if (c.Utility)
            labels["genwave.role"] = "utility";

        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Id"] = c.Id,
            ["Names"] = new[] { c.Name },
            ["State"] = "exited",
            ["Labels"] = labels,
        });
    }

    /// <summary>A docker daemon: applies label filters by exact equality, answers self-inspect.</summary>
    static FakeHttpMessageHandler Daemon(string? selfProject, Func<HttpRequestMessage, HttpResponseMessage?>? intercept = null) =>
        new((request, _) =>
        {
            if (intercept?.Invoke(request) is { } intercepted)
                return Task.FromResult(intercepted);

            var uri = request.RequestUri!;
            if (uri.AbsolutePath == "/containers/json")
            {
                var filters = System.Web.HttpUtility.ParseQueryString(uri.Query)["filters"];
                var wanted = filters is null
                    ? null
                    : JsonDocument.Parse(filters).RootElement.GetProperty("label")[0].GetString();
                var rows = Socket.Where(c => wanted is null || $"{ProjectLabel}={c.Project}" == wanted);
                return Task.FromResult(Json($"[{string.Join(",", rows.Select(ContainerJson))}]"));
            }

            if (uri.AbsolutePath == $"/containers/{Environment.MachineName}/json")
            {
                return Task.FromResult(selfProject is null
                    ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                    : Json(JsonSerializer.Serialize(new { Config = new { Labels = new Dictionary<string, string> { [ProjectLabel] = selfProject } } })));
            }

            // per-container stats / inspect: the rows degrade to nulls, which these specs don't read
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        });

    static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    static DockerContainerStatsSource Source(FakeHttpMessageHandler daemon, string configuredProject = "") =>
        new(
            new HttpClient(daemon),
            new FakeOptionsMonitor<DockerStatsOptions>(new DockerStatsOptions { BaseUrl = BaseUrl, ComposeProject = configuredProject }),
            new ComposeProjectScope(),
            NullLogger<DockerContainerStatsSource>.Instance);

    static string? ListFilter(FakeHttpMessageHandler daemon)
    {
        var list = daemon.Requests.Single(r => r.RequestUri!.AbsolutePath == "/containers/json");
        return System.Web.HttpUtility.ParseQueryString(list.RequestUri!.Query)["filters"];
    }

    static string SelfInspectPath => $"/containers/{Environment.MachineName}/json";

    static bool IsSelfInspect(HttpRequestMessage request) => request.RequestUri!.AbsolutePath == SelfInspectPath;

    static int SelfInspectCount(FakeHttpMessageHandler daemon) => daemon.Requests.Count(IsSelfInspect);

    static string?[] ListFilters(FakeHttpMessageHandler daemon) =>
        daemon.Requests
            .Where(r => r.RequestUri!.AbsolutePath == "/containers/json")
            .Select(r => System.Web.HttpUtility.ParseQueryString(r.RequestUri!.Query)["filters"])
            .ToArray();

    static string[] Names(ContainerStatsReportDto report) => report.Containers.Select(row => row.Name).ToArray();

    public sealed class ScenarioApiLearnsItsProjectFromItsOwnContainer : IAsyncLifetime
    {
        readonly FakeHttpMessageHandler daemon;
        ContainerStatsReportDto report = new(false, null, []);

        public ScenarioApiLearnsItsProjectFromItsOwnContainer()
        {
            // Given an api whose own container is labeled project "genwave", on a socket shared
            // with foreign + fixture containers
            daemon = Daemon(selfProject: "genwave");
        }

        public async Task InitializeAsync()
        {
            // When the report is built
            report = await Source(daemon).GetReportAsync(CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheListingCarriesTheExactProjectLabelFilter()
        {
            // Then the listing was filtered server-side on exactly that label
            Assert.Equal(ExpectedFilter, ListFilter(daemon));
        }

        [Fact]
        public void OnlyTheStationsOwnServiceIsListed()
        {
            // Then the roster is exactly the station's api
            Assert.Equal(["api"], Names(report));
        }

        [Fact]
        public void TheReportIsNotDegraded()
        {
            // Then the report is not degraded
            Assert.False(report.Degraded);
        }

        [Fact]
        public void AForeignContainerIsAbsent()
        {
            // Then the foreign buildx builder is not listed
            Assert.DoesNotContain(report.Containers, row => row.Name.Contains("buildx", StringComparison.Ordinal));
        }

        [Fact]
        public void AUtilityContainerInsideTheProjectIsStillAbsent()
        {
            // Then the in-project utility helper is not listed
            Assert.DoesNotContain(report.Containers, row => row.Name == "ollama-init");
        }

        [Fact]
        public void ALookAlikeProjectIsAbsent()
        {
            // Given a fixture project "genwave-t434c-2c93c722c5" beside "genwave"
            // Then label equality (not a prefix match) keeps it out
            Assert.DoesNotContain(report.Containers, row => row.Name == "t434c-db");
        }
    }

    public sealed class ScenarioTheProjectIsLearnedOncePerProcess : IAsyncLifetime
    {
        readonly FakeHttpMessageHandler daemon = Daemon("genwave");

        public async Task InitializeAsync()
        {
            // Given one source (one ComposeProjectScope)
            var source = Source(daemon);

            // When the report is built twice
            await source.GetReportAsync(CancellationToken.None);
            await source.GetReportAsync(CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSelfInspectHappensOncePerProcessNotPerPoll()
        {
            // Then the api inspected itself a single time
            Assert.Equal(1, SelfInspectCount(daemon));
        }
    }

    public sealed class ScenarioConfiguredProjectOverrides : IAsyncLifetime
    {
        readonly FakeHttpMessageHandler daemon;

        public ScenarioConfiguredProjectOverrides()
        {
            // Given COMPOSE_PROJECT_NAME / DockerStats:ComposeProject set, and a daemon that would
            // report a different project for the api
            daemon = Daemon(selfProject: "something-else");
        }

        public async Task InitializeAsync()
        {
            // When the report is built
            await Source(daemon, configuredProject: "genwave").GetReportAsync(CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheConfiguredNameIsFilteredOn()
        {
            // Then it filters on the configured name
            Assert.Equal(ExpectedFilter, ListFilter(daemon));
        }

        [Fact]
        public void TheApiNeverAsksTheDaemonWhoItIs()
        {
            // Then no self-inspect happened
            Assert.Equal(0, SelfInspectCount(daemon));
        }

        [Fact]
        public void TheFilterIsUrlEncodedJsonOnTheListingPath()
        {
            // Then the filter is URL-encoded JSON on the listing path (a pure ListPath check; it needs no arrange)
            Assert.Equal(
                "/containers/json?all=true&filters=" + Uri.EscapeDataString(ExpectedFilter),
                DockerContainerStatsSource.ListPath("genwave"));
        }
    }

    public sealed class SadPathProjectUnknownFallsBackToEverything : IAsyncLifetime
    {
        readonly FakeHttpMessageHandler daemon;
        ContainerStatsReportDto report = new(false, null, []);

        public SadPathProjectUnknownFallsBackToEverything()
        {
            // Given an api that cannot inspect itself (proxy 403s it) and no configured project
            daemon = Daemon(selfProject: null);
        }

        public async Task InitializeAsync()
        {
            // When the report is built
            report = await Source(daemon).GetReportAsync(CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheListingCarriesNoFilter()
        {
            // Then it asks for the unfiltered listing, as before gh-#629
            Assert.Null(ListFilter(daemon));
        }

        [Fact]
        public void EveryNonUtilityContainerOnTheSocketIsListed()
        {
            // The utility container stays hidden even in fallback; the other three are listed.
            Assert.Equal(3, report.Containers.Count);
        }

        [Fact]
        public void TheLookAlikeProjectIsListedBecauseNothingScopesTheRoster()
        {
            // Then the look-alike project is listed, because nothing scopes the roster
            Assert.Contains(report.Containers, row => row.Name == "t434c-db");
        }
    }

    public sealed class SadPathSelfInspectIsRetriedWhenTransient : IAsyncLifetime
    {
        ContainerStatsReportDto first = new(false, null, []);
        ContainerStatsReportDto second = new(false, null, []);

        public async Task InitializeAsync()
        {
            // Given a proxy whose self-inspect fails once with a 500, then recovers
            var failures = 1;
            var daemon = Daemon("genwave", request =>
                IsSelfInspect(request) && failures-- > 0
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : null);
            var source = Source(daemon);

            // When the report is built twice
            first = await source.GetReportAsync(CancellationToken.None);
            second = await source.GetReportAsync(CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheFirstPollFallsBackToEverything()
        {
            // Then the first poll falls back to every container
            Assert.Equal(3, first.Containers.Count);
        }

        [Fact]
        public void TheSecondPollIsScoped()
        {
            // Then the second poll is scoped to the project
            Assert.Equal(["api"], Names(second));
        }
    }

    public sealed class SadPathOwnContainerCarriesNoProjectLabel : IAsyncLifetime
    {
        readonly FakeHttpMessageHandler daemon = Daemon("genwave", request =>
            IsSelfInspect(request) ? Json("""{"Config":{"Labels":{}}}""") : null);

        public async Task InitializeAsync()
        {
            // Given a self-inspect that answers 200 with no com.docker.compose.project label
            var source = Source(daemon);

            // When the report is built twice
            await source.GetReportAsync(CancellationToken.None);
            await source.GetReportAsync(CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheListingCarriesNoFilter()
        {
            // Then both listings are unfiltered
            Assert.All(ListFilters(daemon), Assert.Null);
        }

        [Fact]
        public void TheAnswerIsSettledSoTheSelfInspectHappensOnce()
        {
            // Then the self-inspect happened once across both polls
            Assert.Equal(1, SelfInspectCount(daemon));
        }
    }

    public sealed class SadPathOwnContainerIsNotFound : IAsyncLifetime
    {
        readonly FakeHttpMessageHandler daemon = Daemon("genwave", request =>
            IsSelfInspect(request) ? new HttpResponseMessage(HttpStatusCode.NotFound) : null);

        public async Task InitializeAsync()
        {
            // Given a self-inspect that answers 404
            var source = Source(daemon);

            // When the report is built twice
            await source.GetReportAsync(CancellationToken.None);
            await source.GetReportAsync(CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheListingCarriesNoFilter()
        {
            // Then both listings are unfiltered
            Assert.All(ListFilters(daemon), Assert.Null);
        }

        [Fact]
        public void TheAnswerIsSettledSoTheSelfInspectHappensOnce()
        {
            // Then the self-inspect happened once across both polls
            Assert.Equal(1, SelfInspectCount(daemon));
        }
    }

    public sealed class ScenarioThroughTheHealthContainersEndpoint : IAsyncLifetime
    {
        readonly FakeHttpMessageHandler daemon = Daemon(selfProject: null);
        HttpStatusCode status;
        string[] names = [];

        public async Task InitializeAsync()
        {
            // Given the real pipeline with the project configured and a shared-socket fake daemon
            await using var factory = new RosterScopeWebFactory(daemon);
            var client = factory.CreateClient();
            var login = await client.PostAsJsonAsync("/api/auth/login", new { password = RosterScopeWebFactory.Password });
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

            // When the Health page's endpoint is called
            var response = await client.GetAsync("/api/health/containers");
            status = response.StatusCode;
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            names = body.RootElement.GetProperty("containers").EnumerateArray()
                .Select(row => row.GetProperty("name").GetString() ?? "").ToArray();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheEndpointAnswersOk()
        {
            // Then the endpoint answers 200
            Assert.Equal(HttpStatusCode.OK, status);
        }

        [Fact]
        public void TheDaemonWasAskedForTheExactProjectFilter()
        {
            // Then the daemon was asked for the exact project filter
            Assert.Equal(ExpectedFilter, ListFilter(daemon));
        }

        [Fact]
        public void OnlyTheStationsApiComesBack()
        {
            // Then only the station's api comes back
            Assert.Equal(["api"], names);
        }
    }
}
