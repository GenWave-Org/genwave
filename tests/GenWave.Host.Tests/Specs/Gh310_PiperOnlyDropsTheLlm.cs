// gh-#310 — the piper-only overlay must drop the LLM pair too, and gh-#309 — a bare
// `docker compose down` must match what launch.sh actually launched.
//
// BDD specification — xUnit, the Gh242 render idiom (Category=Integration for the docker-CLI
// scenarios; the launch.sh --dry-run scenarios run in the ordinary suite, no daemon touched).
//
// gh-#310: HARDWARE.md's recommended Pi shape is "playout + piper-only, no LLM (templated patter,
// by design)". The piper-only overlay once stacked on another layer that carried ollama, and the
// overlay only removed kokoro, so ollama rode along: llama3.2:3b held permanently resident behind
// a 6144M fence LARGER than a 4GB box's whole RAM, with ollama-init pulling ~2GB of weights on
// first boot. The overlay exists because a 4GB box cannot afford kokoro's ~1.2GiB.
//
// gh-#309: a launch may stack overlays on compose.yaml, but a bare `docker compose down` in the
// repo loads only compose.yaml, so services that exist ONLY in an overlay survive the teardown.
// launch.sh now records the file stack as COMPOSE_FILE in .env.

using System.Diagnostics;
using System.Text.Json;

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureComposePiperOnlyDropsTheLlm
{
    static JsonDocument RenderConfig(bool piperOnlyOverlay)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            WorkingDirectory = RepoRootLocator.Find(AppContext.BaseDirectory),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var args = new List<string> { "compose", "-f", "compose.yaml" };
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
            // gh-#249: explicit-but-empty shadows both ambient COMPOSE_PROFILES and a dev box's
            // repo-root .env value, so the render sees the same profile set CI does.
            ["COMPOSE_PROFILES"] = "",
            // Defense in depth, not a requirement: gh-#309 makes launch.sh write COMPOSE_FILE
            // into that same .env, and an explicit -f list DOES outrank it (verified empirically
            // against Compose v5.0.2 — `-f compose.yaml` with COMPOSE_FILE naming an overlay
            // pair renders only compose.yaml's services). Shadowed anyway so this spec's
            // hermeticity never rests on that precedence rule quietly changing.
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

    public static class ScenarioThePiperOnlyOverlayRendersOnTheBase
    {
        static readonly Lazy<JsonDocument> PiperOnly = new(() => RenderConfig(piperOnlyOverlay: true));

        [Fact]
        [Trait("Category", "Integration")]
        public static void The_piper_only_overlay_renders_on_the_bare_base()
        {
            // `-f compose.yaml -f compose.piper-only.yaml` must render on its own: the overlay
            // may only name services the base defines. No compose file defines ollama any more,
            // so this proves the overlay renders on the bare base and the LLM pair stays gone.
            // A bad render throws on a non-zero exit and the spec fails loudly.
            Assert.False(HasService(PiperOnly.Value, "ollama"));
            Assert.True(HasService(PiperOnly.Value, "piper"));
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void The_broadcast_path_survives_intact()
        {
            // What the Pi is actually for: playout + the piper sidecar.
            foreach (var name in new[] { "db", "icecast", "engine", "api", "piper" })
                Assert.True(HasService(PiperOnly.Value, name), $"expected {name} in the piper-only render");
        }
    }

    public static class ScenarioBareComposeCommandsMatchTheLaunch
    {
        // --dry-run only, driving the real toolchain on the developer's own PATH, not a scratch
        // bin — ScriptProcess still starts the child from its sanitized environment (gh-#776).
        static readonly string RealPath = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin";

        static string DryRunPlan(string? preset, params string[] args)
        {
            var envFile = preset is null ? null : Path.Combine(TempDir.CreateForProcessLifetime(), ".env");
            if (envFile is not null) File.WriteAllText(envFile, $"GW_PRESET={preset}\n");
            return ScriptProcess.Run("launch.sh", RealPath, envFile, args: [.. args, "--dry-run"]).StdOut;
        }

        [Fact]
        public static void The_home_flow_records_the_pins_overlay()
        {
            // gh-#309's repro: overlay-only services survived a bare `docker compose down` because it
            // loaded compose.yaml alone. The home preset stacks compose.pinned.yaml
            // (the GHCR image pins) on compose.yaml.
            Assert.Contains(
                "record COMPOSE_FILE=compose.yaml:compose.pinned.yaml in .env",
                DryRunPlan("home"), StringComparison.Ordinal);
        }

        [Fact]
        public static void The_piper_only_home_flow_records_all_three()
        {
            // Three: base, pins, piper-only.
            Assert.Contains(
                "record COMPOSE_FILE=compose.yaml:compose.pinned.yaml:compose.piper-only.yaml in .env",
                DryRunPlan("home-piper-only"), StringComparison.Ordinal);
        }

        [Fact]
        public static void The_dev_flow_records_compose_yaml_explicitly()
        {
            // Explicitly, not empty: a previous home-preset run's value must be REPLACED, or a plain
            // ./launch.sh leaves the box's bare commands pointing at the published-image pair.
            Assert.Contains(
                "record COMPOSE_FILE=compose.yaml in .env",
                DryRunPlan(null), StringComparison.Ordinal);
        }

        [Fact]
        public static void The_record_step_lands_after_the_up_it_describes()
        {
            // Only a stack that actually came up is this box's state.
            var plan = DryRunPlan("home");
            var lines = plan.Split('\n').Where(l => l.StartsWith("plan> ", StringComparison.Ordinal)).ToArray();
            var up = Array.FindLastIndex(lines, l => l.Contains("up -d", StringComparison.Ordinal));
            var record = Array.FindIndex(lines, l => l.Contains("record COMPOSE_FILE", StringComparison.Ordinal));
            Assert.True(record > up, $"expected the record step after the final up; plan:\n{plan}");
        }
    }
}
