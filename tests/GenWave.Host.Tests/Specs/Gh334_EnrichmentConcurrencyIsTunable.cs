// gh-#334 — enrichment saturated every core of a 4-core box, and a pinned appliance box had no
// lever to lower it.
//
// BDD specification — xUnit, the Gh310_PiperOnlyDropsTheLlm.cs render idiom (Category=Integration:
// these shell out to the docker CLI for `docker compose config`, no daemon or stack required).
//
// Two defects, pinned separately below:
//
//   1. compose.piper-only.yaml — the overlay HARDWARE.md topology (a) prescribes — inherited base
//      compose's concurrency of 4. On the Pi 5 field box that meant the api sitting at 378% of
//      400%, the SoC at 83.4°C (past the 80°C soft limit) and the 5V rail dipping into
//      undervoltage every ~2 min. The overlay drops kokoro and the LLM pair for exactly this
//      "a 4GB box cannot afford it" reason; enrichment was the one that got away.
//
//   2. The value was hardcoded, so there was no `.env` lever — and compose.demo.yaml sets
//      Admin__Enabled=false, so PUT /api/settings (the live STORY-139 path this setting was built
//      for) is unreachable on a pinned box too. Neither route worked: an operator on an appliance
//      box could not change it at all without hand-writing an override outside the sanctioned
//      file set.

using System.Diagnostics;
using System.Text.Json;

namespace GenWave.Host.Tests.Specs;

public static class FeatureEnrichmentConcurrencyIsTunable
{
    const string Key = "Library__EnrichmentConcurrency";

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return dir.FullName;
    }

    /// <summary>
    /// Renders a file stack via `docker compose config --format json`. <paramref name="concurrency"/>
    /// seeds LIBRARY_ENRICHMENT_CONCURRENCY in the child environment; null leaves it unset so the
    /// compose-file default is what gets exercised.
    /// </summary>
    static JsonDocument RenderConfig(bool demoOverlay, bool piperOnlyOverlay, string? concurrency = null)
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
        args.AddRange(["config", "--format", "json"]);
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["POSTGRES_PASSWORD"] = "gh334-dummy",
            ["LIBRARY_DB_PASSWORD"] = "gh334-dummy",
            ["STATION_DB_PASSWORD"] = "gh334-dummy",
            ["ICECAST_SOURCE_PASSWORD"] = "gh334-dummy",
            ["ICECAST_ADMIN_PASSWORD"] = "gh334-dummy",
            ["ADMIN_PASSWORD"] = "gh334-dummy",
            ["MEDIA_DIR"] = Path.GetTempPath(),
            ["PUBLIC_HOST"] = "gh334.invalid",
            // Same hermeticity shims as gh-#310's render: a dev box's own .env must not sway these.
            ["COMPOSE_PROFILES"] = "",
            ["COMPOSE_FILE"] = "",
        })
        {
            startInfo.Environment[key] = value;
        }

        // Explicitly REMOVED rather than left alone when the scenario wants the file default —
        // a developer with this exported would otherwise silently pass the default assertions.
        if (concurrency is null)
            startInfo.Environment.Remove("LIBRARY_ENRICHMENT_CONCURRENCY");
        else
            startInfo.Environment["LIBRARY_ENRICHMENT_CONCURRENCY"] = concurrency;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start docker compose config");
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"docker compose config failed (exit {process.ExitCode}): {stdErr}");

        return JsonDocument.Parse(stdOut);
    }

    static string EnrichmentConcurrency(JsonDocument render) =>
        render.RootElement
            .GetProperty("services").GetProperty("api")
            .GetProperty("environment").GetProperty(Key)
            .GetString() ?? throw new InvalidOperationException($"{Key} absent from the render");

    // ---------------------------------------------------------------------
    // The low-memory topology gets the low-core default
    // ---------------------------------------------------------------------

    public static class ScenarioThePiperOnlyOverlayLowersTheDefault
    {
        static readonly Lazy<JsonDocument> Appliance =
            new(() => RenderConfig(demoOverlay: true, piperOnlyOverlay: true));

        static readonly Lazy<JsonDocument> Dev =
            new(() => RenderConfig(demoOverlay: false, piperOnlyOverlay: true));

        [Fact]
        [Trait("Category", "Integration")]
        public static void The_appliance_stack_enriches_two_files_at_a_time()
        {
            // ./launch.sh --pinned --piper-only — HARDWARE.md topology (a), the shape the Pi 5
            // field box runs. 4 pinned all four of its cores.
            Assert.Equal("2", EnrichmentConcurrency(Appliance.Value));
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void The_dev_piper_only_stack_lowers_it_too()
        {
            // ./launch.sh --piper-only, no demo layer. The overlay's reason for existing — a small
            // box — does not depend on which stack it is merged into.
            Assert.Equal("2", EnrichmentConcurrency(Dev.Value));
        }
    }

    // ---------------------------------------------------------------------
    // Every other box is byte-identical to before
    // ---------------------------------------------------------------------

    public static class ScenarioTheDefaultTopologyIsUnchanged
    {
        [Fact]
        [Trait("Category", "Integration")]
        public static void The_plain_dev_stack_still_enriches_four_at_a_time()
        {
            // Making the value a variable must not move any existing box off its current setting.
            Assert.Equal("4",
                EnrichmentConcurrency(RenderConfig(demoOverlay: false, piperOnlyOverlay: false)));
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void The_demo_stack_without_the_overlay_still_enriches_four_at_a_time()
        {
            Assert.Equal("4",
                EnrichmentConcurrency(RenderConfig(demoOverlay: true, piperOnlyOverlay: false)));
        }
    }

    // ---------------------------------------------------------------------
    // The operator's lever — gh-#334's second and worse defect
    // ---------------------------------------------------------------------

    public static class ScenarioAnOperatorCanOverrideEitherStack
    {
        [Fact]
        [Trait("Category", "Integration")]
        public static void The_variable_overrides_the_base_default()
        {
            Assert.Equal("1",
                EnrichmentConcurrency(RenderConfig(demoOverlay: true, piperOnlyOverlay: false, concurrency: "1")));
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void The_variable_overrides_the_piper_only_default_upward_too()
        {
            // The overlay lowers the DEFAULT, it does not impose a ceiling. A Pi 5 on a proven
            // supply with active cooling may want its cores back — and on a pinned box this env
            // var is the only route, since compose.demo.yaml's Admin__Enabled=false closes
            // PUT /api/settings.
            Assert.Equal("4",
                EnrichmentConcurrency(RenderConfig(demoOverlay: true, piperOnlyOverlay: true, concurrency: "4")));
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void The_appliance_stack_really_does_close_the_live_settings_path()
        {
            // Pins the premise the env lever exists FOR. If this ever flips to true, the
            // hardcoded-value defect stops being unreachable-by-design and this whole scenario
            // should be revisited rather than silently kept.
            var admin = RenderConfig(demoOverlay: true, piperOnlyOverlay: true).RootElement
                .GetProperty("services").GetProperty("api")
                .GetProperty("environment").GetProperty("Admin__Enabled").GetString();

            Assert.Equal("false", admin);
        }
    }
}
