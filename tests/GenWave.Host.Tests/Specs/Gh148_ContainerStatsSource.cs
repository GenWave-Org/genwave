// gh-#148 — Health page container stats: source parsing, cpu% math, degrade rules
//
// BDD specification — xUnit. Drives DockerContainerStatsSource end-to-end through a
// FakeHttpMessageHandler serving real-shaped Docker Engine API payloads (captured live through
// the pinned tecnativa/docker-socket-proxy:v0.4.2 against Docker 29.2.1, trimmed to the parsed
// fields plus representative noise), and DockerCpuCalculator directly for the formula edges.
// No spec here reaches the network.

using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Host.Options;
using GenWave.Host.Stats;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

public static class FeatureContainerStatsSource
{
    const string BaseUrl = "http://proxy.test:2375";

    // Real /containers/json shape: PascalCase, leading-slash names, compose labels on managed
    // containers. Row 1 is compose-managed + running; row 2 is an unlabeled exited container.
    const string ContainerListJson = """
        [
          {
            "Id": "ce23d1e36dea0be43b7a272fd82e283cf647ca7392d2bebc11cf0548bd493514",
            "Names": ["/genwave-api-1"],
            "Image": "genwave-api",
            "State": "running",
            "Status": "Up 36 hours (healthy)",
            "Labels": {
              "com.docker.compose.project": "genwave",
              "com.docker.compose.service": "api"
            }
          },
          {
            "Id": "dacbd6262dc9b965d5b57710c753c1d9acf06d80e9c767836181fc0fe73aec28",
            "Names": ["/standalone-box"],
            "Image": "busybox",
            "State": "exited",
            "Status": "Exited (0) 2 hours ago",
            "Labels": {}
          }
        ]
        """;

    // Real one-shot stats shape (snake_case; precpu_stats populated because stream=false makes the
    // daemon take two samples). Crafted numbers: cpuDelta 300e6, systemDelta 6e9, 4 cpus
    // ⇒ (300e6 / 6e9) × 4 × 100 = 20.0%. Memory: 500e6 usage − 100e6 inactive_file = 400e6 used.
    const string StatsJson = """
        {
          "read": "2026-07-29T17:09:48.513038153Z",
          "preread": "2026-07-29T17:09:47.508370615Z",
          "name": "/genwave-api-1",
          "cpu_stats": {
            "cpu_usage": { "total_usage": 400000000, "usage_in_kernelmode": 87737284 },
            "system_cpu_usage": 16000000000,
            "online_cpus": 4
          },
          "precpu_stats": {
            "cpu_usage": { "total_usage": 100000000 },
            "system_cpu_usage": 10000000000,
            "online_cpus": 4
          },
          "memory_stats": {
            "usage": 500000000,
            "stats": { "inactive_file": 100000000, "active_anon": 157765632 },
            "limit": 2000000000
          }
        }
        """;

    const string ApiInspectJson = """
        {
          "Id": "ce23d1e36dea0be43b7a272fd82e283cf647ca7392d2bebc11cf0548bd493514",
          "RestartCount": 3,
          "State": { "Status": "running", "Health": { "Status": "healthy" } }
        }
        """;

    // A container whose image defines no healthcheck: State.Health is absent entirely.
    const string ExitedInspectJson = """
        {
          "Id": "dacbd6262dc9b965d5b57710c753c1d9acf06d80e9c767836181fc0fe73aec28",
          "RestartCount": 0,
          "State": { "Status": "exited" }
        }
        """;

    static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    static DockerContainerStatsSource Source(FakeHttpMessageHandler handler, string baseUrl = BaseUrl) =>
        new(
            new HttpClient(handler),
            new FakeOptionsMonitor<DockerStatsOptions>(new DockerStatsOptions { BaseUrl = baseUrl }),
            NullLogger<DockerContainerStatsSource>.Instance);

    static FakeHttpMessageHandler HealthyStackHandler() =>
        new((request, _) =>
        {
            var path = request.RequestUri?.PathAndQuery ?? "";
            return Task.FromResult(path switch
            {
                "/containers/json?all=true" => Json(ContainerListJson),
                var p when p.Contains("ce23d1e36dea") && p.EndsWith("stats?stream=false", StringComparison.Ordinal) => Json(StatsJson),
                var p when p.Contains("ce23d1e36dea") && p.EndsWith("/json", StringComparison.Ordinal) => Json(ApiInspectJson),
                var p when p.Contains("dacbd6262dc9") && p.EndsWith("/json", StringComparison.Ordinal) => Json(ExitedInspectJson),
                _ => new HttpResponseMessage(HttpStatusCode.Forbidden),
            });
        });

    public sealed class ScenarioHealthyStack
    {
        [Fact]
        public async Task ComposeManagedRowCarriesServiceNameStateHealthCpuMemoryAndRestarts()
        {
            // Given the sidecar serving a compose-managed running container with real-shaped payloads
            var source = Source(HealthyStackHandler());

            // When the report is built
            var report = await source.GetReportAsync(CancellationToken.None);

            // Then the row is fully populated from list + stats + inspect
            Assert.False(report.Degraded);
            Assert.Null(report.Reason);
            var api = Assert.Single(report.Containers, row => row.Name == "api");
            Assert.Equal("running", api.State);
            Assert.Equal("healthy", api.Health);
            Assert.NotNull(api.CpuPercent);
            Assert.Equal(20.0, api.CpuPercent.Value, precision: 6);
            Assert.Equal(400_000_000, api.MemoryUsedBytes);
            Assert.Equal(2_000_000_000, api.MemoryLimitBytes);
            Assert.Equal(3, api.RestartCount);
        }

        [Fact]
        public async Task ExitedUnlabeledRowStripsTheSlashAndSkipsTheStatsCall()
        {
            // Given the same stack — its second container is exited and not compose-managed
            var handler = HealthyStackHandler();
            var source = Source(handler);

            // When the report is built
            var report = await source.GetReportAsync(CancellationToken.None);

            // Then the row falls back to the slash-stripped docker name, null measurements — and
            // no one-shot stats request was ever issued for a container that isn't running
            var exited = Assert.Single(report.Containers, row => row.Name == "standalone-box");
            Assert.Equal("exited", exited.State);
            Assert.Null(exited.Health);
            Assert.Null(exited.CpuPercent);
            Assert.Null(exited.MemoryUsedBytes);
            Assert.Equal(0, exited.RestartCount);
            Assert.DoesNotContain(handler.Requests, request =>
                (request.RequestUri?.PathAndQuery ?? "").Contains("dacbd6262dc9") &&
                (request.RequestUri?.PathAndQuery ?? "").Contains("stats"));
        }

        [Fact]
        public async Task RowsAreNameSorted()
        {
            // Given the two-container stack ("api", "standalone-box")
            var source = Source(HealthyStackHandler());

            // When the report is built
            var report = await source.GetReportAsync(CancellationToken.None);

            // Then rows come back in stable name order
            Assert.Equal(["api", "standalone-box"], report.Containers.Select(row => row.Name).ToArray());
        }
    }

    public sealed class SadPathDegradeRules
    {
        [Fact]
        public async Task UnreachableSidecarDegradesToEmptyListNeverThrows()
        {
            // Given a sidecar that refuses every connection
            var source = Source(new FakeHttpMessageHandler((_, _) =>
                throw new HttpRequestException("connection refused")));

            // When the report is built
            var report = await source.GetReportAsync(CancellationToken.None);

            // Then the envelope is well-formed and degraded — the endpoint never 500s over this
            Assert.True(report.Degraded);
            Assert.NotNull(report.Reason);
            Assert.Contains(BaseUrl, report.Reason);
            Assert.Empty(report.Containers);
        }

        [Fact]
        public async Task EmptyBaseUrlDegradesWithoutTouchingTheNetwork()
        {
            // Given DockerStats:BaseUrl deliberately emptied (feature disabled)
            var handler = new FakeHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
            var source = Source(handler, baseUrl: "");

            // When the report is built
            var report = await source.GetReportAsync(CancellationToken.None);

            // Then it degrades immediately and no request was made at all
            Assert.True(report.Degraded);
            Assert.Empty(report.Containers);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task OneRowsFailedStatsReadDegradesThatRowOnlyNotTheReport()
        {
            // Given a healthy roster whose per-container stats/inspect reads all fail
            var source = Source(new FakeHttpMessageHandler((request, _) =>
            {
                var path = request.RequestUri?.PathAndQuery ?? "";
                return Task.FromResult(path == "/containers/json?all=true"
                    ? Json(ContainerListJson)
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }));

            // When the report is built
            var report = await source.GetReportAsync(CancellationToken.None);

            // Then the roster survives un-degraded; the affected rows read unknown, never zero
            Assert.False(report.Degraded);
            var api = Assert.Single(report.Containers, row => row.Name == "api");
            Assert.Equal("running", api.State);
            Assert.Null(api.CpuPercent);
            Assert.Null(api.MemoryUsedBytes);
            Assert.Null(api.Health);
            Assert.Null(api.RestartCount);
        }
    }

    public sealed class ScenarioServiceNameResolution
    {
        [Fact]
        public void ComposeServiceLabelWinsOverTheContainerName()
        {
            var summary = new DockerContainerSummary
            {
                Id = "abc",
                Names = ["/genwave-api-1"],
                Labels = new Dictionary<string, string> { ["com.docker.compose.service"] = "api" },
            };

            Assert.Equal("api", DockerContainerStatsSource.ResolveServiceName(summary));
        }

        [Fact]
        public void UnlabeledContainerFallsBackToItsSlashStrippedName()
        {
            var summary = new DockerContainerSummary { Id = "abc", Names = ["/gh148-proxy"] };

            Assert.Equal("gh148-proxy", DockerContainerStatsSource.ResolveServiceName(summary));
        }

        [Fact]
        public void NamelessContainerFallsBackToItsShortId()
        {
            var summary = new DockerContainerSummary { Id = "ce23d1e36dea0be43b7a272fd82e283c" };

            Assert.Equal("ce23d1e36dea", DockerContainerStatsSource.ResolveServiceName(summary));
        }
    }

    public sealed class ScenarioMemoryUsedMath
    {
        [Fact]
        public void UsedIsUsageMinusReclaimablePageCache()
        {
            var memory = new DockerMemoryStats
            {
                Usage = 500,
                Stats = new DockerMemoryDetailStats { InactiveFile = 120 },
            };

            Assert.Equal(380, DockerContainerStatsSource.MemoryUsedBytes(memory));
        }

        [Fact]
        public void AnInactiveFileLargerThanUsageFallsBackToRawUsage()
        {
            // Defensive: never a negative "used" out of skewed cgroup counters.
            var memory = new DockerMemoryStats
            {
                Usage = 100,
                Stats = new DockerMemoryDetailStats { InactiveFile = 500 },
            };

            Assert.Equal(100, DockerContainerStatsSource.MemoryUsedBytes(memory));
        }

        [Fact]
        public void MissingUsageIsUnknownNeverZero()
        {
            Assert.Null(DockerContainerStatsSource.MemoryUsedBytes(new DockerMemoryStats()));
            Assert.Null(DockerContainerStatsSource.MemoryUsedBytes(null));
        }
    }
}
