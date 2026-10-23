using System.Diagnostics;
using System.Text.Json.Nodes;
using GenWave.VoiceOff.Audio;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>
/// Drives the real <c>render</c> verb through <see cref="Cli.Run(string[], TextWriter, TextWriter, RenderServices, CancellationToken)"/> over fixture
/// engine and gauntlet folders in a temp root, with stub engines, a fake docker and a scripted resource probe. The real loudness match and ffmpeg run.
/// </summary>
internal sealed class RenderScenario : IDisposable
{
    internal const string Eight = "one two three four five six seven eight";

    /// <summary>Two sentences, so the station's speech markup has a break to put a pause in.</summary>
    internal const string TwoSentences = "First sentence is right here. Second sentence follows right after it.";

    private static readonly Lazy<byte[]> LowSineWav = new(() => Synthesize(330));
    private static readonly Lazy<byte[]> HighSineWav = new(() => Synthesize(660));

    private readonly EngineDirectoryFixture engines = new();
    private readonly GauntletFixtureDirectory gauntlets = new();
    private readonly List<EngineStubServer> stubs = [];
    private readonly List<(string File, string Json)> engineFiles = [];

    internal RenderScenario()
    {
        Root = Path.Combine(Path.GetTempPath(), "voiceoff-root-" + Guid.NewGuid().ToString("N"));
    }

    internal string Root { get; }

    /// <summary>
    /// Starts a stub that answers with a real, loudness-measurable WAV. By default the profile's first voice (<c>v1</c>) gets a low tone and
    /// every other voice a high one, so voiceSelect passes; <paramref name="sameAudioForEveryVoice"/> gives all voices the one tone.
    /// </summary>
    internal async Task<EngineStubServer> StartStubAsync(bool sameAudioForEveryVoice = false)
    {
        EngineStubServer stub = sameAudioForEveryVoice
            ? await EngineStubServer.StartAsync(speechBody: LowSineWav.Value)
            : await EngineStubServer.StartAsync(speechBodyForVoice: voice => voice == "v1" ? LowSineWav.Value : HighSineWav.Value);
        stubs.Add(stub);
        return stub;
    }

    /// <summary>Adds an engine profile whose endpoint is <paramref name="endpoint"/>. The first one added is the baseline.</summary>
    internal void AddEngine(string slug, Uri endpoint, bool markupCue = false, string[]? tiers = null, int? timeoutSeconds = null, double? speed = null)
    {
        JsonObject node = EngineProfileJson.Valid(slug, baseline: engineFiles.Count == 0);
        node["endpoint"] = endpoint.AbsoluteUri;
        node["container"] = slug + "-box";
        node["tiers"] = new JsonArray((tiers ?? ["cpu", "gpu"]).Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());
        if (timeoutSeconds is int timeout)
        {
            node["timeoutSeconds"] = timeout;
        }

        if (speed is double s)
        {
            node["speed"] = s;
        }

        if (!markupCue)
        {
            node["markup"] = JsonNode.Parse("""{ "pause": null, "laugh": null, "sigh": null, "breath": null, "um": null, "emphasis": null }""");
        }

        engineFiles.Add((slug + ".json", node.ToJsonString()));
    }

    internal string EnginesDirectory => engines.Create(engineFiles.ToArray());

    /// <summary>A gauntlet folder: valid rules and the given lines (a pending corrections file when asked).</summary>
    internal string GauntletDirectory(string[] lines, bool correctionsPending = false) =>
        gauntlets.Create(
            ("lines.json", GauntletFixtureDirectory.Lines(lines)),
            ("corrections.json", correctionsPending ? """{ "provenance": "pending", "corrections": [] }""" : GauntletFixtureDirectory.ValidCorrections));

    internal static string[] ThreeLines() =>
    [
        GauntletFixtureDirectory.Line("leadin-1", "LeadIn", Eight),
        GauntletFixtureDirectory.Line("stationid-1", "StationId", Eight),
        GauntletFixtureDirectory.Line("emotive-1", "Emotive", Eight),
    ];

    internal static string CrosstalkLine(string id) =>
        $$"""{ "id": "{{id}}", "class": "Crosstalk", "turns": [ { "register": "bubbly", "text": "{{Eight}}" }, { "register": "laconic", "text": "{{Eight}}" } ], "cues": [] }""";

    /// <summary>Runs <c>render</c> with the standard options plus <paramref name="extra"/>; every container is running.</summary>
    internal RenderRun Run(string enginesDirectory, string gauntletDirectory, string tier, params string[] extra) =>
        Run(new FakeDockerInspector(new ContainerState(true, true, "10.0.0.2")), enginesDirectory, gauntletDirectory, tier, extra);

    internal RenderRun Run(FakeDockerInspector docker, string enginesDirectory, string gauntletDirectory, string tier, params string[] extra) =>
        Run(docker, CancellationToken.None, enginesDirectory, gauntletDirectory, tier, extra);

    internal RenderRun Run(FakeDockerInspector docker, CancellationToken ct, string enginesDirectory, string gauntletDirectory, string tier, params string[] extra) =>
        RunAt(Root, docker, ct, enginesDirectory, gauntletDirectory, tier, extra);

    /// <summary>Like <see cref="Run(FakeDockerInspector, CancellationToken, string, string, string, string[])"/> with <c>--root</c> pointing at <paramref name="root"/>.</summary>
    internal RenderRun RunAt(string root, FakeDockerInspector docker, CancellationToken ct, string enginesDirectory, string gauntletDirectory, string tier, params string[] extra)
    {
        // The container answers one reading: the sampler also ticks on a real one-second timer, and later ticks get no answer and are skipped.
        ScriptedResourceProbe probe = new(["310MiB / 4GiB"], [], null);
        using HttpClient http = SpeechClient.CreateHttpClient();
        RenderServices services = new(docker, probe, http, new FixedClockTimeProvider(new DateTimeOffset(2026, 10, 22, 19, 30, 0, TimeSpan.Zero)), new LoudnessMatch(), "test-host");
        string[] args = ["render", "--tier", tier, "--root", root, "--engines", enginesDirectory, "--gauntlet", gauntletDirectory, .. extra];
        StringWriter output = new();
        StringWriter error = new();
        int exit = Cli.Run(args, output, error, services, ct);
        string[] folders = Directory.Exists(root) ? Directory.GetDirectories(root) : [];
        string? runDirectory = folders.Length == 1 ? folders[0] : null;
        return new RenderRun(exit, output.ToString(), error.ToString(), runDirectory, probe.GpuReads);
    }

    public void Dispose()
    {
        foreach (EngineStubServer stub in stubs)
        {
            stub.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        engines.Dispose();
        gauntlets.Dispose();
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    // Three seconds of a 24 kHz mono tone: long enough for loudnorm to measure, and a whole WAV the stub can serve as is.
    private static byte[] Synthesize(int hertz)
    {
        string path = Path.Combine(Path.GetTempPath(), "voiceoff-sine-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            ProcessStartInfo psi = new("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
            foreach (string a in new[] { "-nostdin", "-hide_banner", "-y", "-f", "lavfi", "-i", $"sine=frequency={hertz}:sample_rate=24000", "-t", "3", "-ac", "1", "-c:a", "pcm_s16le", path })
            {
                psi.ArgumentList.Add(a);
            }

            using Process process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg did not start.");
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
