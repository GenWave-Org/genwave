using System.Diagnostics;
using System.Text.Json;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// Runs `docker compose -f compose.yaml [-f overlay ...] config --format json` from the repo root
/// and parses the result. Extracted from Gh242; used by Gh242 and Story487 (Story181, Story202, Gh148,
/// Gh310 and Gh334 still carry their own copies). No daemon is touched: `config` only merges text. Extracted from
/// <c>Gh242_ComposePiperOnlyOverride</c> (PLAN T598 review) so Story487 didn't have to start life as
/// a third near-identical copy of the same render + depends_on-names plumbing.
/// </summary>
internal static class ComposeConfigRender
{
    /// <summary>
    /// Renders compose.yaml plus any overlay files, merged in the given order (last wins, same as
    /// a real `docker compose -f ... -f ...` invocation).
    /// </summary>
    public static JsonDocument Render(params string[] overlayFiles)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            WorkingDirectory = RepoRootLocator.Find(AppContext.BaseDirectory),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var args = new List<string> { "compose", "-f", "compose.yaml" };
        foreach (var overlay in overlayFiles)
        {
            args.Add("-f");
            args.Add(overlay);
        }
        args.AddRange(["config", "--format", "json"]);
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        // Dummy secrets only — same idiom as Story181/Story202/Gh148/Gh242: `config` never reaches
        // a daemon or reads real credentials, it only needs every `${VAR:?}` to resolve.
        foreach (var (key, value) in DummyEnv)
            startInfo.Environment[key] = value;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start docker compose config");
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"docker compose config failed (exit {process.ExitCode}): {stdErr}");

        return JsonDocument.Parse(stdOut);
    }

    /// <summary>The given service's `depends_on` keys, sorted — empty when the service has none.</summary>
    public static string[] DependsOnNames(JsonDocument render, string service)
    {
        var serviceElement = render.RootElement.GetProperty("services").GetProperty(service);
        return serviceElement.TryGetProperty("depends_on", out var dependsOn)
            ? dependsOn.EnumerateObject().Select(p => p.Name).Order().ToArray()
            : [];
    }

    /// <summary>
    /// Parses a rendered compose duration (e.g. "45s") into whole seconds. Every duration this repo's
    /// compose files use (healthcheck interval/timeout/start_period) is authored in seconds — see
    /// compose*.yaml — so a bare `Ns` suffix is the only shape handled.
    /// </summary>
    public static int ParseSecondsDuration(string duration)
    {
        if (!duration.EndsWith('s') || !int.TryParse(duration[..^1], out var seconds))
            throw new FormatException($"expected a whole-seconds compose duration like '45s', got '{duration}'");
        return seconds;
    }

    static readonly IReadOnlyDictionary<string, string> DummyEnv = new Dictionary<string, string>
    {
        ["POSTGRES_PASSWORD"] = "compose-render-dummy",
        ["LIBRARY_DB_PASSWORD"] = "compose-render-dummy",
        ["STATION_DB_PASSWORD"] = "compose-render-dummy",
        ["ICECAST_SOURCE_PASSWORD"] = "compose-render-dummy",
        ["ICECAST_ADMIN_PASSWORD"] = "compose-render-dummy",
        ["ADMIN_PASSWORD"] = "compose-render-dummy",
        ["MEDIA_DIR"] = Path.GetTempPath(),
        ["PUBLIC_HOST"] = "compose-render.invalid",
        // gh-#249: explicit-but-empty shadows both ambient COMPOSE_PROFILES and a dev box's
        // repo-root .env value, so the render sees the same profile set (none) CI does.
        ["COMPOSE_PROFILES"] = "",
    };
}
