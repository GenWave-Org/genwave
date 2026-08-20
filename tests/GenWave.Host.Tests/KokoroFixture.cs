using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace GenWave.Host.Tests;

/// <summary>
/// Brings up a disposable Kokoro TTS container (kokoro-compose.yaml) publishing
/// <c>127.0.0.1:18880</c> so integration tests can hit the real model. On-demand: requires Docker.
/// Tears the container down (<c>down -v</c>) when the test collection finishes.
/// <para>
/// Kokoro takes 30–90 s to warm up (model load). After <c>docker compose up --wait</c> (which waits
/// for the healthcheck TCP probe), this fixture polls <c>POST /v1/audio/speech</c> with a minimal
/// request until the model returns 200 — the port opens before model weights are resident, so
/// "response ended prematurely" errors occur during the warmup window. Budget: 3 minutes.
/// </para>
/// </summary>
public sealed class KokoroFixture : IAsyncLifetime
{
    // Per-instance project name and OS-assigned host port (gh-#602, mirroring #597's
    // DatabaseFixture fix): a fixed name/port pair let two concurrent runs (two checkouts, or CI
    // plus local) fight over one container — the second `up` reused the first's project and either
    // teardown's `down -v` killed the other run's Kokoro mid-suite. Teardown needs no extra
    // scoping: `down -v` was always -p-scoped, so a unique name makes it run-isolated by itself.
    readonly string project = $"genwave-kokorotest-{Guid.NewGuid():N}"[..29];

    static readonly TimeSpan WarmupPollInterval = TimeSpan.FromSeconds(5);
    static readonly TimeSpan WarmupTimeout = TimeSpan.FromMinutes(3);

    string composeFile = "";

    /// <summary>The base URL the disposable Kokoro is reachable on — discovered from the ephemeral
    /// loopback port Docker assigned this run's container, never a fixed constant. Populated by
    /// <see cref="InitializeAsync"/>; collection tests read it off their injected fixture.</summary>
    public string BaseUrl { get; private set; } = "";

    public async Task InitializeAsync()
    {
        composeFile = LocateComposeFile();
        Compose("up", "-d", "--wait");
        BaseUrl = $"http://127.0.0.1:{DiscoverHostPort()}";
        await WaitForModelAsync();
    }

    public Task DisposeAsync()
    {
        try { Compose("down", "-v"); } catch { /* best-effort teardown */ }
        return Task.CompletedTask;
    }

    async Task WaitForModelAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var body = JsonSerializer.Serialize(new { input = "ready", voice = "af_heart", response_format = "wav" });
        var deadline = Stopwatch.GetTimestamp() + (long)(WarmupTimeout.TotalSeconds * Stopwatch.Frequency);

        while (true)
        {
            try
            {
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                using var response = await http.PostAsync(BaseUrl + "/v1/audio/speech", content);
                if (response.IsSuccessStatusCode) return;
            }
            catch (Exception)
            {
                // Port open but model not yet ready — keep polling.
            }

            if (Stopwatch.GetTimestamp() > deadline)
                throw new InvalidOperationException(
                    $"Kokoro model did not become ready within {WarmupTimeout.TotalMinutes:F0} minutes.");

            await Task.Delay(WarmupPollInterval);
        }
    }

    int DiscoverHostPort()
    {
        // `docker compose port kokoro 8880` prints the mapping's host side (e.g. "127.0.0.1:32771").
        var output = RunCapture(
            "docker", ["compose", "-p", project, "-f", composeFile, "port", "kokoro", "8880"]);
        var portText = output.Trim().Split(':')[^1];

        return int.TryParse(portText, out var port) && port > 0
            ? port
            : throw new InvalidOperationException($"could not parse a host port from 'docker compose port' output: '{output.Trim()}'");
    }

    void Compose(params string[] verbAndArgs)
    {
        var args = new List<string> { "compose", "-p", project, "-f", composeFile };
        args.AddRange(verbAndArgs);
        Run("docker", args);
    }

    static void Run(string file, IReadOnlyList<string> args) => RunCapture(file, args);

    static string RunCapture(string file, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"failed to start {file}");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException(
                $"{file} {string.Join(' ', args)} failed:\n{stderr.Result}{stdout.Result}");

        return stdout.Result;
    }

    static string LocateComposeFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return Path.Combine(dir.FullName, "tests", "GenWave.Host.Tests", "kokoro-compose.yaml");
    }
}

/// <summary>
/// xUnit collection definition — all tests in [Collection("Kokoro")] share a single
/// <see cref="KokoroFixture"/> instance and therefore a single container lifetime.
/// </summary>
[CollectionDefinition("Kokoro")]
public sealed class KokoroCollection : ICollectionFixture<KokoroFixture>;
