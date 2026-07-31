// gh-#242 — the piper-only / no-kokoro overlay's compose pins
//
// BDD specification — xUnit, Story202 render idiom + Story201 launch-plan idiom. Render-driving
// scenarios carry Category=Integration (docker CLI); the launch.sh --dry-run scenarios run in the
// ordinary suite (no daemon touched, same as Story201).
//
// Pins under guard: the DEFAULT render is untouched (kokoro present, api still hard-depends on it,
// no EngineByKind seed — the gh-#242 constraint that existing boxes never change behaviour on
// upgrade); the piper-only overlay removes kokoro, resets api's depends_on to db+engine only, and
// seeds Tts:EngineByKind covering EVERY SegmentKind -> "piper" (asserted against the enum itself,
// so adding a seventh kind fails here until the overlay learns it); the overlay stacks cleanly on
// compose.demo.yaml; and launch.sh --piper-only merges the overlay file LAST in both flows.

using System.Diagnostics;
using System.Text.Json;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Specs;

public static class FeatureComposePiperOnlyOverride
{
    const string OverlayFile = "compose.piper-only.yaml";

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return dir.FullName;
    }

    static JsonDocument RenderConfig(bool demoOverlay, bool piperOnlyOverlay)
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
        // The overlay always merges LAST — the same ordering launch.sh --piper-only produces —
        // so its kokoro removal + depends_on reset win over anything the demo overlay merged.
        if (piperOnlyOverlay) { args.Add("-f"); args.Add(OverlayFile); }
        args.AddRange(new[] { "config", "--format", "json" });
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        // Same dummy-secret idiom as Story181/Story202/Gh148: `config` only merges text, no
        // daemon reached.
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["POSTGRES_PASSWORD"] = "gh242-dummy",
            ["LIBRARY_DB_PASSWORD"] = "gh242-dummy",
            ["STATION_DB_PASSWORD"] = "gh242-dummy",
            ["ICECAST_SOURCE_PASSWORD"] = "gh242-dummy",
            ["ICECAST_ADMIN_PASSWORD"] = "gh242-dummy",
            ["ADMIN_PASSWORD"] = "gh242-dummy",
            ["MEDIA_DIR"] = Path.GetTempPath(),
            ["PUBLIC_HOST"] = "gh242.invalid",
            // gh-#249: explicit-but-empty shadows BOTH ambient COMPOSE_PROFILES and a dev
            // box's repo-root .env value, so the render sees the same profile set (none)
            // CI does. Overlay/flag-selected profiles are unaffected — a --profile flag
            // takes precedence over this variable entirely (verified empirically).
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

    static string[] DependsOnNames(JsonDocument render, string service) =>
        render.RootElement.GetProperty("services").GetProperty(service)
            .GetProperty("depends_on").EnumerateObject().Select(p => p.Name).Order().ToArray();

    public static class ScenarioDefaultExperienceUnchanged
    {
        static readonly Lazy<JsonDocument> Base = new(() => RenderConfig(demoOverlay: false, piperOnlyOverlay: false));

        [Fact]
        [Trait("Category", "Integration")]
        public static void The_default_render_carries_exactly_todays_service_set_kokoro_included()
        {
            // The gh-#242 constraint: kokoro stays on by default for every existing box. The full
            // set is pinned (not just "kokoro present") so the overlay landing can never have
            // touched the base file's service roster unnoticed.
            var services = Base.Value.RootElement.GetProperty("services")
                .EnumerateObject().Select(p => p.Name).Order().ToArray();
            Assert.Equal(new[] { "api", "db", "dockerproxy", "engine", "icecast", "kokoro", "piper" }, services);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void Api_still_hard_depends_on_kokoro_by_default()
        {
            Assert.Equal(new[] { "db", "engine", "kokoro" }, DependsOnNames(Base.Value, "api"));
            Assert.Equal("service_healthy",
                Base.Value.RootElement.GetProperty("services").GetProperty("api")
                    .GetProperty("depends_on").GetProperty("kokoro").GetProperty("condition").GetString());
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void No_engine_by_kind_seed_leaks_into_the_default_render()
        {
            // Empty/absent Tts:EngineByKind is F70.3's own "byte-identical to pre-feature routing"
            // default — the overlay's all-piper seed must never reach a box that didn't opt in.
            var env = Base.Value.RootElement.GetProperty("services").GetProperty("api").GetProperty("environment");
            Assert.False(env.TryGetProperty("Tts__EngineByKind", out _));
        }
    }

    public static class ScenarioPiperOnlyRender
    {
        static readonly Lazy<JsonDocument> BasePiperOnly = new(() => RenderConfig(demoOverlay: false, piperOnlyOverlay: true));

        [Fact]
        [Trait("Category", "Integration")]
        public static void Kokoro_is_absent_from_the_render()
        {
            // The overlay's profile assignment ("disabled-by-piper-only", activated by nothing)
            // removes the service from config/up/pull on the default profile set.
            Assert.False(BasePiperOnly.Value.RootElement.GetProperty("services").TryGetProperty("kokoro", out _));
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void Api_depends_only_on_db_and_engine()
        {
            // The `depends_on: !override` reset — without it the merged render is invalid
            // ("api depends on undefined service kokoro") and nothing would boot at all.
            var render = BasePiperOnly.Value;
            Assert.Equal(new[] { "db", "engine" }, DependsOnNames(render, "api"));
            var dependsOn = render.RootElement.GetProperty("services").GetProperty("api").GetProperty("depends_on");
            Assert.Equal("service_healthy", dependsOn.GetProperty("db").GetProperty("condition").GetString());
            Assert.Equal("service_healthy", dependsOn.GetProperty("engine").GetProperty("condition").GetString());
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void Engine_by_kind_seed_maps_every_segment_kind_to_piper()
        {
            // Asserted against the enum itself, not a copied list: a seventh SegmentKind fails
            // this spec until the overlay learns it — otherwise the new kind's renders would
            // take the health-based path chasing a kokoro that doesn't exist.
            var env = BasePiperOnly.Value.RootElement.GetProperty("services").GetProperty("api").GetProperty("environment");
            using var map = JsonDocument.Parse(env.GetProperty("Tts__EngineByKind").GetString()!);
            var mapped = map.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetString(), StringComparer.Ordinal);

            Assert.Equal(Enum.GetNames<SegmentKind>().Order().ToArray(), mapped.Keys.Order().ToArray());
            Assert.All(mapped.Values, engine => Assert.Equal("piper", engine));
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void The_piper_sidecar_and_its_fallback_endpoint_survive_the_overlay()
        {
            // Non-empty Tts:Fallback:Endpoint is load-bearing (F70.1): empty means
            // FallbackTtsSynthesizer is a kokoro-only pass-through and the map above is moot.
            var render = BasePiperOnly.Value;
            Assert.True(render.RootElement.GetProperty("services").TryGetProperty("piper", out _));
            Assert.Equal("http://piper:5000",
                render.RootElement.GetProperty("services").GetProperty("api")
                    .GetProperty("environment").GetProperty("Tts__Fallback__Endpoint").GetString());
        }
    }

    public static class ScenarioPiperOnlyStacksOnTheDemoOverlay
    {
        static readonly Lazy<JsonDocument> DemoPiperOnly = new(() => RenderConfig(demoOverlay: true, piperOnlyOverlay: true));

        [Fact]
        [Trait("Category", "Integration")]
        public static void Kokoro_is_absent_and_the_depends_on_reset_holds_after_the_demo_merge()
        {
            var render = DemoPiperOnly.Value;
            Assert.False(render.RootElement.GetProperty("services").TryGetProperty("kokoro", out _));
            Assert.Equal(new[] { "db", "engine" }, DependsOnNames(render, "api"));
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void The_demo_topology_itself_is_untouched()
        {
            // caddy still exists and the engine-by-kind seed merged into the demo api env
            // alongside (mapping-key merge) rather than replacing it.
            //
            // ollama used to be asserted present HERE, as part of "untouched". gh-#310 is exactly
            // the finding that it should never have been: this overlay's whole reason for existing
            // is that a 4GB box cannot afford kokoro's ~1.2GiB, and it was shipping an
            // always-resident llama3.2:3b twice that size underneath. The LLM pair's absence is
            // now pinned by FeatureComposePiperOnlyDropsTheLlm (gh-#310).
            var services = DemoPiperOnly.Value.RootElement.GetProperty("services");
            Assert.True(services.TryGetProperty("caddy", out _));
            var env = services.GetProperty("api").GetProperty("environment");
            Assert.True(env.TryGetProperty("Tts__EngineByKind", out _));
            Assert.Equal("false", env.GetProperty("Admin__Enabled").GetString());
        }
    }

    public static class ScenarioLaunchScriptFlag
    {
        static (int ExitCode, string StdOut, string StdErr) RunLaunch(params string[] args)
        {
            var startInfo = new ProcessStartInfo("bash")
            {
                WorkingDirectory = RepoRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(Path.Combine(RepoRoot(), "launch.sh"));
            foreach (var arg in args) startInfo.ArgumentList.Add(arg);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("failed to start launch.sh");
            var stdOut = process.StandardOutput.ReadToEnd();
            var stdErr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, stdOut, stdErr);
        }

        static string[] PlanLines(string stdOut) =>
            stdOut.Split('\n').Where(l => l.StartsWith("plan> ", StringComparison.Ordinal)).ToArray();

        static readonly Lazy<(int ExitCode, string StdOut, string StdErr)> Dev =
            new(() => RunLaunch("--piper-only", "--dry-run"));

        static readonly Lazy<(int ExitCode, string StdOut, string StdErr)> Pinned =
            new(() => RunLaunch("--pinned", "--piper-only", "--dry-run"));

        [Fact]
        public static void Dev_flow_merges_the_overlay_into_every_compose_command()
        {
            Assert.Equal(0, Dev.Value.ExitCode);
            Assert.All(
                PlanLines(Dev.Value.StdOut).Where(l => l.Contains("compose", StringComparison.Ordinal)),
                l => Assert.Contains(OverlayFile, l, StringComparison.Ordinal));
        }

        [Fact]
        public static void Dev_flow_passes_the_overlay_to_migrate()
        {
            Assert.Contains(PlanLines(Dev.Value.StdOut), l =>
                l.Contains("migrate.sh", StringComparison.Ordinal) && l.Contains(OverlayFile, StringComparison.Ordinal));
        }

        [Fact]
        public static void Pinned_flow_merges_the_overlay_after_the_demo_overlay()
        {
            // LAST wins the merge — the overlay must follow compose.demo.yaml on every command.
            Assert.Equal(0, Pinned.Value.ExitCode);
            var composeLines = PlanLines(Pinned.Value.StdOut)
                .Where(l => l.Contains("compose", StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(composeLines);
            Assert.All(composeLines, l =>
            {
                var demoAt = l.IndexOf("compose.demo.yaml", StringComparison.Ordinal);
                var piperOnlyAt = l.IndexOf(OverlayFile, StringComparison.Ordinal);
                Assert.True(demoAt >= 0 && piperOnlyAt > demoAt,
                    $"expected {OverlayFile} after compose.demo.yaml in: {l}");
            });
        }

        [Fact]
        public static void Plain_flows_never_mention_the_overlay()
        {
            // The default experience stays byte-identical: no --piper-only, no overlay file.
            var dev = RunLaunch("--dry-run");
            var pinned = RunLaunch("--pinned", "--dry-run");
            Assert.DoesNotContain(PlanLines(dev.StdOut), l => l.Contains(OverlayFile, StringComparison.Ordinal));
            Assert.DoesNotContain(PlanLines(pinned.StdOut), l => l.Contains(OverlayFile, StringComparison.Ordinal));
        }
    }
}
