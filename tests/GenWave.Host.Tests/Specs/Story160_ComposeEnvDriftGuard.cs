// STORY-160 — The fresh-deploy test mirror cannot drift from compose (Epic Z / SPEC F63,
// closes gitea-#235).
//
// BDD specification — xUnit. A repo-content fact (the Story107/Story151 idiom) parses
// compose.yaml and asserts two-way parity with the Story151 fresh-deploy env mirror
// (FeatureSeededDefaults.ComposeApiEnvMirror in Story151_SeededDefaults.cs). Test-only:
// compose.yaml is READ, never edited — the zero-diff hash pin at the gate still applies.

using System.Text.RegularExpressions;

namespace GenWave.Host.Tests.Specs;

public static class FeatureComposeEnvDriftGuard
{
    // ─────────────────────────────────────────────────────────────────────────
    // Shared helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Repo root, resolved relative to the test assembly's build output — the Story074/Story102/
    /// Story107/Story151 RepoRoot convention for reaching repo-root files from a test project.
    /// </summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string ComposeYamlPath => Path.Combine(RepoRoot, "compose.yaml");

    static string ComposeYamlText => File.ReadAllText(ComposeYamlPath);

    /// <summary>
    /// Parses the raw double-underscore env var names out of the <c>api</c> service's
    /// <c>environment:</c> block in compose.yaml — comments and blank lines skipped. No YAML
    /// package ships with this test project, so this is a targeted line parser (the
    /// Story107/Story151 repo-content-fact idiom), bounded to the exact indentation compose.yaml
    /// uses today. It fails loudly — via <see cref="Assert.True(bool, string)"/> — the moment the
    /// <c>api:</c> service or its <c>environment:</c> block can't be located, so a compose
    /// restructure breaks this fact instead of silently parsing zero keys.
    /// </summary>
    internal static IReadOnlyList<string> ParseComposeApiEnvironmentKeys(string composeYamlText)
    {
        var lines = composeYamlText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        var apiServiceStart = Array.FindIndex(lines, line => line == "  api:");
        Assert.True(apiServiceStart >= 0,
            $"could not locate the '  api:' service block in {ComposeYamlPath}.");

        // The api service block runs until the next line at the same (2-space) service indent —
        // i.e. the next service key, or EOF.
        var apiServiceEnd = lines.Length;
        for (var i = apiServiceStart + 1; i < lines.Length; i++)
        {
            if (Regex.IsMatch(lines[i], @"^  \S"))
            {
                apiServiceEnd = i;
                break;
            }
        }

        var environmentStart = -1;
        for (var i = apiServiceStart + 1; i < apiServiceEnd; i++)
        {
            if (lines[i] == "    environment:")
            {
                environmentStart = i;
                break;
            }
        }
        Assert.True(environmentStart >= 0,
            $"could not locate the api service's '    environment:' block in {ComposeYamlPath}.");

        var keys = new List<string>();
        for (var i = environmentStart + 1; i < apiServiceEnd; i++)
        {
            var line = lines[i];

            // A line indented less than the env-entry level (6 spaces) ends the block — the next
            // top-level api property (volumes:, healthcheck:, ...).
            if (!line.StartsWith("      ", StringComparison.Ordinal)) break;

            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            var match = Regex.Match(trimmed, "^([A-Za-z0-9_]+):");
            Assert.True(match.Success,
                $"could not parse an env var name from {ComposeYamlPath} line: '{line}'.");
            keys.Add(match.Groups[1].Value);
        }

        Assert.True(keys.Count > 0,
            $"parsed zero env vars from the api service's environment block in {ComposeYamlPath}.");

        return keys;
    }

    /// <summary>ASP.NET Core's double-underscore env var convention: <c>A__B</c> binds to config key <c>A:B</c>.</summary>
    internal static string ToConfigKey(string doubleUnderscoreEnvKey) =>
        doubleUnderscoreEnvKey.Replace("__", ":", StringComparison.Ordinal);

    /// <summary>
    /// Compose <c>api</c> env vars that are NOT settings-relevant — secrets and infra wiring the
    /// Settings API deliberately never exposes through <c>StationSettingsAllowlist</c>. Every
    /// other <c>api</c> env var is asserted to have a matching entry, colon-formed, in
    /// <see cref="FeatureSeededDefaults.ComposeApiEnvMirror"/>.
    /// </summary>
    internal static readonly IReadOnlySet<string> ExcludedApiEnvVars = new HashSet<string>(StringComparer.Ordinal)
    {
        // DB credentials — env-only secrets; StationSettingsAllowlist.cs's header names
        // ConnectionStrings:* explicitly as deliberately absent from the allowlist.
        "ConnectionStrings__Library",
        "ConnectionStrings__Station",
        // Admin login secret — same StationSettingsAllowlist.cs exclusion.
        "Admin__Password",
        // Liquidsoap control-socket wiring — fixed container-network addressing, not an
        // operator-editable setting (no StationSettingsAllowlist entry).
        "Liquidsoap__Host",
        "Liquidsoap__Port",
        "Liquidsoap__QueueId",
        // Library media mount path — infra, changing it requires a volume remount, not a live PUT.
        "Library__MediaRoot",
        // Fixed station identity (row key), unlike Station:Name/Voice which ARE operator-editable.
        "Station__Id",
        // TTS render format/cache mount — infra, not operator-editable.
        "Tts__Format",
        "Tts__CacheRoot",
        // Public listener (SPEC F64.1/F64.2, STORY-172, PLAN T15). ASPNETCORE_URLS is Kestrel/host
        // wiring, not a GenWave config key at all. Spectator__PublicPort is env/compose-only —
        // SpectatorOptions's doc comment names the same StationSettingsAllowlist exclusion as
        // Admin__Password/ProxyOptions: flipping it requires a container recreate, never a live PUT.
        "ASPNETCORE_URLS",
        "Spectator__PublicPort",
    };

    /// <summary>
    /// Direction 1 of F63.1's two-way parity: every mirror key must resolve, double-underscored,
    /// to a real compose <c>api</c> env var. Throws a plain <see cref="InvalidOperationException"/>
    /// naming the offending key (rather than asserting internally) so it can be exercised directly
    /// against synthetic input — the same "assertion helper meta-test" shape as the Z8 icon-
    /// tooltip walker (<c>assertNoUnlabeledIconOnlyButtons</c>).
    /// </summary>
    internal static void AssertMirrorKeysExistInCompose(
        IReadOnlyCollection<string> mirrorConfigKeys,
        IReadOnlyCollection<string> composeApiEnvKeys)
    {
        var composeConfigKeys = new HashSet<string>(
            composeApiEnvKeys.Select(ToConfigKey), StringComparer.OrdinalIgnoreCase);

        foreach (var mirrorKey in mirrorConfigKeys)
        {
            if (!composeConfigKeys.Contains(mirrorKey))
            {
                throw new InvalidOperationException(
                    $"Story151 fresh-deploy mirror key '{mirrorKey}' has no matching env var in " +
                    "compose.yaml's api service environment block (SPEC F63.1) — remove it from " +
                    "FeatureSeededDefaults.ComposeApiEnvMirror or restore the compose.yaml env var.");
            }
        }
    }

    /// <summary>
    /// Direction 2 of F63.1's two-way parity: every settings-relevant compose <c>api</c> env var
    /// (i.e. every one not in <see cref="ExcludedApiEnvVars"/>) must resolve, colon-formed, to a
    /// mirror key. Same "plain exception naming the key" shape as
    /// <see cref="AssertMirrorKeysExistInCompose"/>, for the same meta-test reason.
    /// </summary>
    internal static void AssertSettingsRelevantComposeVarsExistInMirror(
        IReadOnlyCollection<string> composeApiEnvKeys,
        IReadOnlyCollection<string> mirrorConfigKeys,
        IReadOnlySet<string> excludedApiEnvVars)
    {
        var mirrorKeySet = new HashSet<string>(mirrorConfigKeys, StringComparer.OrdinalIgnoreCase);

        foreach (var envKey in composeApiEnvKeys)
        {
            if (excludedApiEnvVars.Contains(envKey)) continue;

            var configKey = ToConfigKey(envKey);
            if (!mirrorKeySet.Contains(configKey))
            {
                throw new InvalidOperationException(
                    $"compose.yaml api env var '{envKey}' ('{configKey}') is settings-relevant but " +
                    "missing from the Story151 fresh-deploy mirror (SPEC F63.1) — add it to " +
                    "FeatureSeededDefaults.ComposeApiEnvMirror.");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — two-way parity
    // ─────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioTwoWayParityHolds
    {
        [Fact]
        public void EveryMirrorKeyExistsInComposeApiEnvironment()
        {
            var composeApiEnvKeys = ParseComposeApiEnvironmentKeys(ComposeYamlText);

            AssertMirrorKeysExistInCompose(FeatureSeededDefaults.ComposeApiEnvMirror.Keys.ToList(), composeApiEnvKeys);
        }

        [Fact]
        public void EverySettingsRelevantComposeApiEnvVarExistsInTheMirror()
        {
            var composeApiEnvKeys = ParseComposeApiEnvironmentKeys(ComposeYamlText);

            AssertSettingsRelevantComposeVarsExistInMirror(
                composeApiEnvKeys, FeatureSeededDefaults.ComposeApiEnvMirror.Keys.ToList(), ExcludedApiEnvVars);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioDriftBreaksTheBuild
    {
        [Fact]
        public void AParityFailureNamesTheMissingKey()
        {
            // Direction 1: a mirror key with no compose counterpart names itself.
            var mirrorOnlyEx = Record.Exception(() => AssertMirrorKeysExistInCompose(
                mirrorConfigKeys: ["Station:DoesNotExist"],
                composeApiEnvKeys: ["Station__Name"]));

            Assert.NotNull(mirrorOnlyEx);
            Assert.Contains("Station:DoesNotExist", mirrorOnlyEx.Message, StringComparison.Ordinal);

            // Direction 2: a settings-relevant compose var with no mirror counterpart names itself.
            var composeOnlyEx = Record.Exception(() => AssertSettingsRelevantComposeVarsExistInMirror(
                composeApiEnvKeys: ["Station__Voice"],
                mirrorConfigKeys: [],
                excludedApiEnvVars: new HashSet<string>(StringComparer.Ordinal)));

            Assert.NotNull(composeOnlyEx);
            Assert.Contains("Station__Voice", composeOnlyEx.Message, StringComparison.Ordinal);

            // A renamed key on either side is indistinguishable from a removal to these helpers —
            // the renamed name simply fails to match, which is exactly what the two assertions
            // above already exercise (the "add" side sees a key with no counterpart, same as a
            // straight removal would).
        }
    }
}
