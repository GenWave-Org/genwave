// STORY-202 — Log shipper behind the logging profile
//
// BDD specification — xUnit (SPEC F78.1, F78.3, F78.4, F78.5). Renders the real compose
// config (Story181 idiom, dummy secrets) and inspects the alloy service; the label
// contract is asserted against observability/alloy/config.alloy + observability/LABELS.md.
//
// Render-driving scenarios need the docker CLI → Category=Integration. File-contract
// scenarios run in the ordinary suite. AC6 (fail-loud on empty LOKI_PUSH_URL) starts a
// real container, so it is Integration and remains the T49 acceptance to verify
// empirically before unskipping (F77.3 precedent: never assume image behavior).
//
// Pending until T49 (/build-loop unskips).

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAlloyLoggingProfile
{
    const string Pending = "pending: T49 — alloy logging profile (unskip in /build-loop)";

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return dir.FullName;
    }

    static JsonDocument RenderConfig(bool loggingProfile, bool demoOverlay)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var args = new List<string> { "compose", "-f", "compose.yaml" };
        if (demoOverlay) { args.Add("-f"); args.Add("compose.demo.yaml"); }
        if (loggingProfile) { args.Add("--profile"); args.Add("logging"); }
        args.AddRange(new[] { "config", "--format", "json" });
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        // Same dummy-secret idiom as Story181: `config` only merges text, no daemon reached.
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["POSTGRES_PASSWORD"] = "story202-dummy",
            ["LIBRARY_DB_PASSWORD"] = "story202-dummy",
            ["STATION_DB_PASSWORD"] = "story202-dummy",
            ["ICECAST_SOURCE_PASSWORD"] = "story202-dummy",
            ["ICECAST_ADMIN_PASSWORD"] = "story202-dummy",
            ["ADMIN_PASSWORD"] = "story202-dummy",
            ["MEDIA_DIR"] = Path.GetTempPath(),
            ["PUBLIC_HOST"] = "story202.invalid",
            // Deliberately NO LOKI_* here — F78.4: rendering must succeed with them unset.
        })
        {
            startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start docker compose config");
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"docker compose config failed (exit {process.ExitCode}): {stdErr}");

        return JsonDocument.Parse(stdOut);
    }

    public static class ScenarioProfilePosture
    {
        static readonly Lazy<JsonDocument> WithLogging = new(() => RenderConfig(loggingProfile: true, demoOverlay: false));
        static readonly Lazy<JsonDocument> WithoutLogging = new(() => RenderConfig(loggingProfile: false, demoOverlay: false));

        [Fact(Skip = Pending)]
        [Trait("Category", "Integration")]
        public static void Alloy_is_absent_without_the_profile()
        {
            Assert.False(WithoutLogging.Value.RootElement.GetProperty("services").TryGetProperty("alloy", out _));
        }

        [Fact(Skip = Pending)]
        [Trait("Category", "Integration")]
        public static void Alloy_exists_with_the_profile()
        {
            Assert.True(WithLogging.Value.RootElement.GetProperty("services").TryGetProperty("alloy", out _));
        }

        [Fact(Skip = Pending)]
        [Trait("Category", "Integration")]
        public static void Alloy_image_is_pinned_by_tag()
        {
            var image = WithLogging.Value.RootElement.GetProperty("services").GetProperty("alloy").GetProperty("image").GetString()!;
            Assert.Matches(new Regex(@"^grafana/alloy:(?!latest$).+"), image);
        }

        [Fact(Skip = Pending)]
        [Trait("Category", "Integration")]
        public static void Alloy_publishes_no_host_port()
        {
            Assert.False(WithLogging.Value.RootElement.GetProperty("services").GetProperty("alloy").TryGetProperty("ports", out _));
        }

        [Fact(Skip = Pending)]
        [Trait("Category", "Integration")]
        public static void No_service_hard_depends_on_alloy()
        {
            var services = WithLogging.Value.RootElement.GetProperty("services");
            var dependents = new List<string>();
            foreach (var service in services.EnumerateObject())
            {
                if (service.Value.TryGetProperty("depends_on", out var dependsOn)
                    && dependsOn.ValueKind == JsonValueKind.Object
                    && dependsOn.TryGetProperty("alloy", out _))
                {
                    dependents.Add(service.Name);
                }
            }
            Assert.Empty(dependents);
        }

        [Fact(Skip = Pending)]
        [Trait("Category", "Integration")]
        public static void Alloy_declares_a_readiness_healthcheck()
        {
            var alloy = WithLogging.Value.RootElement.GetProperty("services").GetProperty("alloy");
            Assert.True(alloy.TryGetProperty("healthcheck", out var healthcheck)
                && healthcheck.GetProperty("test").ToString().Contains("ready", StringComparison.OrdinalIgnoreCase));
        }

        [Fact(Skip = Pending)]
        [Trait("Category", "Integration")]
        public static void Push_url_env_is_empty_default_when_unset()
        {
            // F78.4: ${LOKI_PUSH_URL:-} — render succeeded with the var unset, value empty
            var env = WithLogging.Value.RootElement.GetProperty("services").GetProperty("alloy").GetProperty("environment");
            Assert.Equal("", env.GetProperty("LOKI_PUSH_URL").GetString());
        }
    }

    public static class ScenarioPublishGuardIndifference
    {
        [Fact(Skip = Pending)]
        [Trait("Category", "Integration")]
        public static void Publish_guard_exits_zero_with_logging_profile_active()
        {
            // Given the base+demo render with the logging profile
            // When  fed to tools/check-compose-publish.sh --config-file
            // Then  exit 0 — activating logging can never introduce a publish (F78.1)
            using var render = RenderConfig(loggingProfile: true, demoOverlay: true);
            var fixturePath = Path.Combine(Path.GetTempPath(), $"story202-publish-{Guid.NewGuid():N}.json");
            File.WriteAllText(fixturePath, render.RootElement.GetRawText());
            try
            {
                var startInfo = new ProcessStartInfo("bash")
                {
                    WorkingDirectory = RepoRoot(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                startInfo.ArgumentList.Add(Path.Combine(RepoRoot(), "tools", "check-compose-publish.sh"));
                startInfo.ArgumentList.Add("--config-file");
                startInfo.ArgumentList.Add(fixturePath);
                using var process = Process.Start(startInfo)!;
                var stdOut = process.StandardOutput.ReadToEnd();
                var stdErr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                Assert.True(process.ExitCode == 0, $"expected exit 0\nstdout:\n{stdOut}\nstderr:\n{stdErr}");
            }
            finally
            {
                File.Delete(fixturePath);
            }
        }
    }

    public static class ScenarioLabelContract
    {
        static readonly string[] ContractLabels = ["service", "station", "env"];

        [Fact(Skip = Pending)]
        public static void Labels_doc_declares_exactly_the_contract_labels()
        {
            // observability/LABELS.md lists indexed labels as `- \`<name>\`` bullets
            var labelsDoc = File.ReadAllText(Path.Combine(RepoRoot(), "observability", "LABELS.md"));
            var declared = Regex.Matches(labelsDoc, @"^- `([a-z_]+)`", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value).Order().ToArray();
            Assert.Equal(ContractLabels.Order().ToArray(), declared);
        }

        [Fact(Skip = Pending)]
        public static void Alloy_config_indexes_exactly_the_contract_labels()
        {
            // The delivery-side label block in config.alloy is delimited by the markers
            // `// labels:begin` / `// labels:end` (part of the T49 contract) so the indexed
            // set is extractable without an Alloy parser.
            var config = File.ReadAllText(Path.Combine(RepoRoot(), "observability", "alloy", "config.alloy"));
            var block = Regex.Match(config, @"// labels:begin(.*?)// labels:end", RegexOptions.Singleline).Groups[1].Value;
            var indexed = Regex.Matches(block, @"^\s*""?([a-z_]+)""?\s*=", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value).Distinct().Order().ToArray();
            Assert.Equal(ContractLabels.Order().ToArray(), indexed);
        }
    }

    public static class SadPathFailLoudOnEmptyPushUrl
    {
        [Fact(Skip = Pending)]
        [Trait("Category", "Integration")]
        public static void Container_refuses_to_run_without_a_push_url()
        {
            // Given the logging profile active and LOKI_PUSH_URL empty
            // When  the alloy container is run one-shot (`compose --profile logging run --rm alloy`)
            // Then  it exits non-zero — never runs silently without shipping (F78.4).
            // T49 verifies the pinned image's actual behavior empirically before unskipping.
            var startInfo = new ProcessStartInfo("docker")
            {
                WorkingDirectory = RepoRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in new[] { "compose", "-f", "compose.yaml", "--profile", "logging", "run", "--rm", "--no-deps", "alloy" })
                startInfo.ArgumentList.Add(arg);
            startInfo.Environment["POSTGRES_PASSWORD"] = "story202-dummy";
            startInfo.Environment["LIBRARY_DB_PASSWORD"] = "story202-dummy";
            startInfo.Environment["STATION_DB_PASSWORD"] = "story202-dummy";
            startInfo.Environment["ICECAST_SOURCE_PASSWORD"] = "story202-dummy";
            startInfo.Environment["ICECAST_ADMIN_PASSWORD"] = "story202-dummy";
            startInfo.Environment["ADMIN_PASSWORD"] = "story202-dummy";
            startInfo.Environment["MEDIA_DIR"] = Path.GetTempPath();
            startInfo.Environment["LOKI_PUSH_URL"] = "";

            using var process = Process.Start(startInfo)!;
            var completed = process.WaitForExit(TimeSpan.FromSeconds(60));
            if (!completed) process.Kill(entireProcessTree: true);

            Assert.True(completed && process.ExitCode != 0,
                completed ? $"expected non-zero exit, got {process.ExitCode}" : "alloy kept running with an empty push URL");
        }
    }
}
