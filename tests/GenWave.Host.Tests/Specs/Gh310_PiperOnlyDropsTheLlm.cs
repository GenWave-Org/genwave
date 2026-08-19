// gh-#310 — the piper-only overlay must drop the LLM pair too, and gh-#309 — a bare
// `docker compose down` must match what launch.sh actually launched.
//
// BDD specification — xUnit, the Gh242 render idiom (Category=Integration for the docker-CLI
// scenarios; the launch.sh --dry-run scenarios run in the ordinary suite, no daemon touched).
//
// gh-#310: HARDWARE.md topology (a) — the RECOMMENDED Pi shape — is "playout + piper-only, no LLM
// (templated patter, by design)" and prescribes `./launch.sh --pinned --piper-only`. That command
// stacks this overlay on compose.demo.yaml, and the overlay only removed kokoro, so ollama rode
// along: llama3.2:3b held permanently resident behind a 6144M fence LARGER than a 4GB box's whole
// RAM, with ollama-init pulling ~2GB of weights on first boot. The overlay exists because a 4GB box
// cannot afford kokoro's ~1.2GiB.
//
// gh-#309: `--pinned` runs against compose.yaml + compose.demo.yaml, but a bare `docker compose
// down` in the repo loads only compose.yaml — so caddy/ollama/ollama-init, which exist ONLY in an
// overlay, survive the teardown. launch.sh now records the file stack as COMPOSE_FILE in .env.

using System.Diagnostics;
using System.Text.Json;

namespace GenWave.Host.Tests.Specs;

public static class FeatureComposePiperOnlyDropsTheLlm
{
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
        if (piperOnlyOverlay) { args.Add("-f"); args.Add("compose.piper-only.yaml"); }
        args.AddRange(new[] { "config", "--format", "json" });
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["POSTGRES_PASSWORD"] = "gh310-dummy",
            ["LIBRARY_DB_PASSWORD"] = "gh310-dummy",
            ["STATION_DB_PASSWORD"] = "gh310-dummy",
            ["ICECAST_SOURCE_PASSWORD"] = "gh310-dummy",
            ["ICECAST_ADMIN_PASSWORD"] = "gh310-dummy",
            ["ADMIN_PASSWORD"] = "gh310-dummy",
            ["MEDIA_DIR"] = Path.GetTempPath(),
            ["PUBLIC_HOST"] = "gh310.invalid",
            // gh-#249: explicit-but-empty shadows both ambient COMPOSE_PROFILES and a dev box's
            // repo-root .env value, so the render sees the same profile set CI does.
            ["COMPOSE_PROFILES"] = "",
            // Defense in depth, not a requirement: gh-#309 makes launch.sh write COMPOSE_FILE
            // into that same .env, and an explicit -f list DOES outrank it (verified empirically
            // against Compose v5.0.2 — `-f compose.yaml` with COMPOSE_FILE naming the demo pair
            // renders no caddy). Shadowed anyway so this spec's hermeticity never rests on that
            // precedence rule quietly changing.
            ["COMPOSE_FILE"] = "",
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

    static bool HasService(JsonDocument render, string name) =>
        render.RootElement.GetProperty("services").TryGetProperty(name, out _);

    public static class ScenarioTheApplianceStackDropsTheLlm
    {
        static readonly Lazy<JsonDocument> Render = new(() => RenderConfig(demoOverlay: true, piperOnlyOverlay: true));

        [Fact]
        [Trait("Category", "Integration")]
        public static void Ollama_is_absent() => Assert.False(HasService(Render.Value, "ollama"));

        [Fact]
        [Trait("Category", "Integration")]
        public static void The_weight_puller_is_absent_too()
        {
            // The pair MUST travel together: ollama-init's `depends_on: ollama` would name a
            // profile-disabled service and fail the entire render.
            Assert.False(HasService(Render.Value, "ollama-init"));
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void Kokoro_is_still_absent_as_well()
        {
            // The gh-#242 removal must survive alongside the new one.
            Assert.False(HasService(Render.Value, "kokoro"));
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void The_broadcast_path_survives_intact()
        {
            // What the Pi is actually for: playout + the piper sidecar + the front door.
            foreach (var name in new[] { "db", "icecast", "engine", "api", "piper", "caddy" })
                Assert.True(HasService(Render.Value, name), $"expected {name} in the piper-only appliance render");
        }
    }

    public static class ScenarioTheDevStackRendersWithoutADemoLayer
    {
        [Fact]
        [Trait("Category", "Integration")]
        public static void Naming_a_demo_only_service_does_not_break_the_bare_dev_render()
        {
            // ollama/ollama-init exist ONLY in compose.demo.yaml, so `-f compose.yaml -f
            // compose.piper-only.yaml` declares two services the base never defined. An inactive
            // profile excludes the block before Compose asks it for an image — if that ever stops
            // holding, this render throws on a non-zero exit and the spec fails loudly.
            var render = RenderConfig(demoOverlay: false, piperOnlyOverlay: true);
            Assert.False(HasService(render, "ollama"));
            Assert.True(HasService(render, "piper"));
        }
    }

    public static class ScenarioTheDefaultDemoStackKeepsItsBrain
    {
        [Fact]
        [Trait("Category", "Integration")]
        public static void A_demo_box_without_the_overlay_still_gets_ollama()
        {
            // The opt-out is the overlay's, never the default's — a 16GB demo box is exactly who
            // the resident model is right for.
            var render = RenderConfig(demoOverlay: true, piperOnlyOverlay: false);
            Assert.True(HasService(render, "ollama"));
            Assert.True(HasService(render, "ollama-init"));
            Assert.True(HasService(render, "kokoro"));
        }
    }

    public static class ScenarioBareComposeCommandsMatchTheLaunch
    {
        static string DryRunPlan(params string[] args)
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
            startInfo.ArgumentList.Add("--dry-run");

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("failed to start launch.sh");
            var stdOut = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return stdOut;
        }

        [Fact]
        public static void The_appliance_flow_records_both_overlay_files()
        {
            // gh-#309's repro: caddy and ollama survived a bare `docker compose down` because it
            // loaded compose.yaml alone. SPEC F136.5 (T317): --pinned now stacks THREE files —
            // compose.pinned.yaml (the GHCR image pins) joined compose.demo.yaml (the public-
            // appliance topology) as a separate overlay.
            Assert.Contains(
                "record COMPOSE_FILE=compose.yaml:compose.pinned.yaml:compose.demo.yaml in .env",
                DryRunPlan("--pinned"), StringComparison.Ordinal);
        }

        [Fact]
        public static void The_piper_only_appliance_flow_records_all_three()
        {
            // Four, post-F136.5 — see the comment above.
            Assert.Contains(
                "record COMPOSE_FILE=compose.yaml:compose.pinned.yaml:compose.demo.yaml:compose.piper-only.yaml in .env",
                DryRunPlan("--pinned", "--piper-only"), StringComparison.Ordinal);
        }

        [Fact]
        public static void The_dev_flow_records_compose_yaml_explicitly()
        {
            // Explicitly, not empty: a previous --pinned run's value must be REPLACED, or a plain
            // ./launch.sh leaves the box's bare commands pointing at the demo pair.
            Assert.Contains(
                "record COMPOSE_FILE=compose.yaml in .env",
                DryRunPlan(), StringComparison.Ordinal);
        }

        [Fact]
        public static void The_record_step_lands_after_the_up_it_describes()
        {
            // Only a stack that actually came up is this box's state.
            var plan = DryRunPlan("--pinned");
            var lines = plan.Split('\n').Where(l => l.StartsWith("plan> ", StringComparison.Ordinal)).ToArray();
            var up = Array.FindLastIndex(lines, l => l.Contains("up -d", StringComparison.Ordinal));
            var record = Array.FindIndex(lines, l => l.Contains("record COMPOSE_FILE", StringComparison.Ordinal));
            Assert.True(record > up, $"expected the record step after the final up; plan:\n{plan}");
        }
    }
}
