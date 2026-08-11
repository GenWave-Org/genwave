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
// Unskipped by T49 — grafana/alloy:v1.18.0 pinned in compose.yaml, observability/alloy/
// config.alloy + observability/LABELS.md wired.

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAlloyLoggingProfile
{
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
            // gh-#249: pin the profile set empty — shadows ambient COMPOSE_PROFILES and a
            // dev box's .env (COMPOSE_PROFILES=logging there would fail Alloy_is_absent_
            // without_the_profile). The --profile logging flag above is unaffected: a
            // --profile flag takes precedence over this variable entirely (verified).
            ["COMPOSE_PROFILES"] = "",
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

        [Fact]
        [Trait("Category", "Integration")]
        public static void Alloy_is_absent_without_the_profile()
        {
            Assert.False(WithoutLogging.Value.RootElement.GetProperty("services").TryGetProperty("alloy", out _));
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void Alloy_exists_with_the_profile()
        {
            Assert.True(WithLogging.Value.RootElement.GetProperty("services").TryGetProperty("alloy", out _));
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void Alloy_image_is_pinned_by_tag()
        {
            var image = WithLogging.Value.RootElement.GetProperty("services").GetProperty("alloy").GetProperty("image").GetString()!;
            Assert.Matches(new Regex(@"^grafana/alloy:(?!latest$).+"), image);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void Alloy_publishes_no_host_port()
        {
            Assert.False(WithLogging.Value.RootElement.GetProperty("services").GetProperty("alloy").TryGetProperty("ports", out _));
        }

        [Fact]
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

        [Fact]
        [Trait("Category", "Integration")]
        public static void Alloy_declares_a_readiness_healthcheck()
        {
            // Must discriminate on the contiguous phrase "is ready", not just "ready": this
            // image's not-ready body is "Alloy is not ready." (200 body "Alloy is ready." once
            // up), which still contains "ready" as a bare substring. A probe that only checks
            // for "ready" (e.g. a bare `grep -qi ready`) reports healthy for BOTH bodies and
            // would fail this assertion — it never emits the discriminator phrase "is ready".
            var alloy = WithLogging.Value.RootElement.GetProperty("services").GetProperty("alloy");
            Assert.True(alloy.TryGetProperty("healthcheck", out var healthcheck)
                && healthcheck.GetProperty("test").ToString().Contains("is ready", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
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
        [Fact]
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

        [Fact]
        public static void Labels_doc_declares_exactly_the_contract_labels()
        {
            // observability/LABELS.md lists indexed labels as `- \`<name>\`` bullets
            var labelsDoc = File.ReadAllText(Path.Combine(RepoRoot(), "observability", "LABELS.md"));
            var declared = Regex.Matches(labelsDoc, @"^- `([a-z_]+)`", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value).Order().ToArray();
            Assert.Equal(ContractLabels.Order().ToArray(), declared);
        }

        [Fact]
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

    // gh-#251 — the icecast metadata-ceremony drop stage: icecast 2.5 logs an 8-line ceremony per
    // liquidsoap metadata push (~75% of the service's volume); config.alloy drops exactly those
    // shapes at ingest, icecast-scoped. The drop expression is delimited by `// drop:begin` /
    // `// drop:end` (the labels-block idiom) so these facts can extract it without an Alloy
    // parser and pin it against REAL captured lines — an icecast upgrade that rewords the
    // ceremony turns into a red fact here, never silently-resumed spam.
    public static class ScenarioIcecastCeremonyDrop
    {
        static string DropExpression()
        {
            var config = File.ReadAllText(Path.Combine(RepoRoot(), "observability", "alloy", "config.alloy"));
            var block = Regex.Match(config, @"// drop:begin(.*?)// drop:end", RegexOptions.Singleline).Groups[1].Value;
            var expression = Regex.Match(block, "expression = \"(.*)\"").Groups[1].Value;
            Assert.False(string.IsNullOrWhiteSpace(expression), "drop:begin/end block with an expression must exist in config.alloy");
            return expression;
        }

        // All five ceremony shapes, captured verbatim from the demo box via Loki, 2026-08-11.
        static readonly string[] CeremonyLines =
        [
            "[2026-08-11  11:59:46] INFO admin/admin_handle_request Received admin command metadata on mount '/stream'",
            "[2026-08-11  11:59:46] WARN admin/admin_enforce_unsafe Client 0x59917577c630 (role=legacy-global-source, acl=legacy-global-source, username=source) uses safe method GET on /admin/metadata",
            "[2026-08-11  11:59:46] WARN admin/command_metadata Metadata request mountpoint /stream contains \"song\" but also \"artist\" and/or \"title\"",
            "[2026-08-11  11:59:46] INFO admin/command_metadata Metadata on mountpoint /stream changed to \"GWAV 108.8 | House of Lords - My Generation\"",
            "[2026-08-11  11:59:46] INFO util/util_conv_string converting metadata from \"UTF-8\" to \"ISO8859-1\"",
            "[2026-08-11  11:59:46] INFO event-stream/event_stream_queue event queued",
        ];

        // Lines that MUST keep shipping. The access-log line (captured) is the adversarial one —
        // it contains "/admin/metadata" yet is gh-#115's acceptance signal; the error and
        // listener lines are constructed to icecast 2.5's shapes (no live capture available in
        // the sampled window) and marked as such.
        static readonly string[] MustSurviveLines =
        [
            "172.28.20.4 - source [09/Aug/2026:19:11:01 +0000] \"GET /admin/metadata HTTP/1.0\" 200 433 \"-\" \"Liquidsoap/2.4.4 (Unix; OCaml 4.14.2)\" 0", // captured
            "[2026-08-09  19:17:20] EROR admin/command_metadata Metadata update failed: connection reset by peer",   // constructed
            "[2026-08-11  11:59:46] INFO source/source_main listener count on /stream now 2",                        // constructed
        ];

        [Fact]
        public static void Every_ceremony_shape_is_dropped()
        {
            var drop = new Regex(DropExpression());
            foreach (var line in CeremonyLines)
                Assert.True(drop.IsMatch(line), $"ceremony line escaped the drop expression:\n{line}");
        }

        [Fact]
        public static void Access_log_errors_and_listener_lines_survive()
        {
            var drop = new Regex(DropExpression());
            foreach (var line in MustSurviveLines)
                Assert.False(drop.IsMatch(line), $"a keep-line would be dropped:\n{line}");
        }

        [Fact]
        public static void Drop_stage_is_scoped_to_the_icecast_service_only()
        {
            // The stage.match selector fences the drop to icecast — every other service's lines
            // must never pass through the drop expression at all.
            var config = File.ReadAllText(Path.Combine(RepoRoot(), "observability", "alloy", "config.alloy"));
            var match = Regex.Match(config, @"stage\.match\s*\{(.*?)stage\.drop", RegexOptions.Singleline).Groups[1].Value;
            Assert.Contains("selector = \"{service=\\\"icecast\\\"}\"", match, StringComparison.Ordinal);
        }
    }

    public static class SadPathFailLoudOnEmptyPushUrl
    {
        [Fact]
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
            // gh-#249: same ambient-profile pin as RenderConfig; --profile logging still wins.
            startInfo.Environment["COMPOSE_PROFILES"] = "";

            using var process = Process.Start(startInfo)!;
            var completed = process.WaitForExit(TimeSpan.FromSeconds(60));
            if (!completed) process.Kill(entireProcessTree: true);

            Assert.True(completed && process.ExitCode != 0,
                completed ? $"expected non-zero exit, got {process.ExitCode}" : "alloy kept running with an empty push URL");
        }
    }
}
