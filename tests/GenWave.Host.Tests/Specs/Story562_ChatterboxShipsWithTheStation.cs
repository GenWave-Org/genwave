// STORY-562 — Chatterbox ships with the station (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. AC1 reads compose.yaml as text; AC2/AC4 run `launch.sh --dry-run`
// (no daemon touched) and ask the docker CLI which services that plan's file set + profiles render
// (`compose config --services` merges text only — the Gh310 render idiom, hence Category=Integration).

using System.Text.RegularExpressions;

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureChatterboxShipsWithTheStation
{
    static string RepoRoot => RepoRootLocator.Find(AppContext.BaseDirectory);

    // The indented block of one top-level service in compose.yaml: from `  <name>:` to the next two-space key.
    static string ServiceBlock(string composeText, string service)
    {
        var lines = composeText.Split('\n');
        var start = Array.FindIndex(lines, l => l == $"  {service}:");
        Assert.True(start >= 0, $"compose.yaml has no service {service}");
        var block = lines.Skip(start + 1).TakeWhile(l => l.Length == 0 || l.StartsWith("   ") || l.TrimStart().StartsWith('#'));
        return string.Join('\n', block);
    }

    // What `./launch.sh [flags] --dry-run` would run, as the services compose renders for it.
    static IReadOnlySet<string> LaunchServiceList(params string[] flags)
    {
        var scratch = TempDir.CreateForProcessLifetime();
        var envFile = Path.Combine(scratch, ".env");
        File.WriteAllText(envFile, "");   // no GW_PRESET / COMPOSE_PROFILES from a dev box's own .env

        var plan = ScriptProcess.Run(
            "launch.sh", Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin", envFile, args: [.. flags, "--dry-run"]).StdOut;
        var upLine = plan.Split('\n').Single(l => l.StartsWith("plan> docker compose") && l.EndsWith(" up -d"));
        var files = Regex.Matches(upLine, @"-f (\S+)").Select(m => m.Groups[1].Value).DefaultIfEmpty("compose.yaml").ToList();
        var profiles = plan.Split('\n').Single(l => l.StartsWith("plan-profiles> ")).Substring("plan-profiles> ".Length).Trim();

        using var render = ComposeConfigRender.RenderWithProfiles(profiles, [.. files.Where(f => f != "compose.yaml")]);
        return ComposeConfigRender.Services(render);
    }

    // AC1 — compose.yaml read as text
    public sealed class ScenarioTheComposeService
    {
        readonly string chatterbox;
        readonly string api;

        public ScenarioTheComposeService()
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot, "compose.yaml"));
            chatterbox = ServiceBlock(text, "chatterbox");
            api = ServiceBlock(text, "api");
        }

        [Fact]
        public void TheCpusReadTheVoiceCpusVariableDefault64() =>
            Assert.Contains("cpus: ${GENWAVE_VOICE_CPUS:-64}", chatterbox);

        [Fact]
        public void TheServiceIsInTheChatterboxProfile() =>
            Assert.Contains("profiles: [\"chatterbox\"]", chatterbox);

        [Fact]
        public void TheClipsVolumeIsReadOnlyInTheEngine() =>
            Assert.Contains("- voice-clips:/clips:ro", chatterbox);

        [Fact]
        public void TheApiMountsTheClipsVolume() =>
            Assert.Matches(@"- voice-clips:/clips(\s|$)", api);
    }

    // AC2 — ./launch.sh --chatterbox dry-run
    public sealed class ScenarioLaunch
    {
        static readonly Lazy<IReadOnlySet<string>> Services = new(() => LaunchServiceList("--chatterbox"));

        [Fact]
        [Trait("Category", "Integration")]
        public void TheServiceListHasChatterbox() =>
            Assert.Contains("chatterbox", Services.Value);

        [Fact]
        [Trait("Category", "Integration")]
        public void TheServiceListHasNoKokoro() =>
            Assert.DoesNotContain("kokoro", Services.Value);
    }

    // Sad path
    // AC4 — ./launch.sh with no flag
    public sealed class ScenarioOldPresets
    {
        [Fact]
        [Trait("Category", "Integration")]
        public void TheServiceListIsUnchanged() =>
            Assert.Equal(
                new HashSet<string> { "api", "db", "dockerproxy", "engine", "icecast", "kokoro", "voice-seed" },
                LaunchServiceList());
    }
}
