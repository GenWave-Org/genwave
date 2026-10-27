using System.Diagnostics;
using System.Text.Json.Nodes;
using GenWave.VoiceOff.Audio;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>
/// Drives the real <c>render</c> verb through <see cref="Cli.Run(string[], TextWriter, TextWriter, RenderServices, CancellationToken)"/> over fixture
/// engine and card folders in a temp root, the repo's real script, stub engines and a fake docker. The real loudness match and ffmpeg run.
/// The first engine added is the baseline; the first card added is the as-shipped card (and so takes no overrides).
/// </summary>
internal sealed class RenderScenario : IDisposable
{
    private static readonly Lazy<byte[]> LowSineWav = new(() => Synthesize(330));

    private readonly EngineDirectoryFixture engines = new();
    private readonly CardFixtureDirectory cards = new();
    private readonly List<EngineStubServer> stubs = [];
    private readonly List<(string File, string Json)> engineFiles = [];
    private readonly List<(string File, string Json)> cardFiles = [];
    private readonly List<string> scriptCopies = [];

    internal RenderScenario()
    {
        Root = Path.Combine(Path.GetTempPath(), "voiceoff-root-" + Guid.NewGuid().ToString("N"));
        ScriptPath = Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "tools", "VoiceOff", "script", "script.json");
    }

    internal string Root { get; }

    /// <summary>The script every run reads; the repo's own unless <see cref="WriteScript"/> was used.</summary>
    internal string ScriptPath { get; set; }

    /// <summary>Starts a stub that answers every speech request with a real, loudness-measurable WAV.</summary>
    internal async Task<EngineStubServer> StartStubAsync(TimeSpan speechDelay = default, int? failSpeechRequest = null)
    {
        EngineStubServer stub = await EngineStubServer.StartAsync(speechDelay: speechDelay, speechBody: LowSineWav.Value, failSpeechRequest: failSpeechRequest);
        stubs.Add(stub);
        return stub;
    }

    /// <summary>Adds an engine profile whose endpoint is <paramref name="endpoint"/>; its container is <c>&lt;slug&gt;-box</c>.</summary>
    internal void AddEngine(string slug, Uri endpoint, int? timeoutSeconds = null)
    {
        JsonObject node = EngineProfileJson.Valid(slug, baseline: engineFiles.Count == 0);
        node["endpoint"] = endpoint.AbsoluteUri;
        node["container"] = slug + "-box";
        if (timeoutSeconds is int timeout)
        {
            node["timeoutSeconds"] = timeout;
        }

        engineFiles.Add((slug + ".json", node.ToJsonString()));
    }

    /// <summary>Adds an engine file as written, for a spec that needs a broken one.</summary>
    internal void AddEngineFile(string fileName, string json) => engineFiles.Add((fileName, json));

    /// <summary>Adds a card file as written, for a spec that needs a broken one.</summary>
    internal void AddCardFile(string fileName, string json) => cardFiles.Add((fileName, json));

    /// <summary>Adds a card on <paramref name="engine"/>; <paramref name="edit"/> may change the card JSON before it is written.</summary>
    internal void AddCard(string id, string engine, Action<JsonObject>? edit = null)
    {
        JsonObject node = JsonNode.Parse(CardFixtureDirectory.CardJson(id, engine))?.AsObject() ?? throw new InvalidOperationException("The card fixture did not parse.");
        if (cardFiles.Count == 0)
        {
            node.Remove("overrides");
            node["asShipped"] = true;
        }

        edit?.Invoke(node);
        cardFiles.Add((id + ".json", node.ToJsonString()));
    }

    /// <summary>Writes a copy of the repo's script changed by <paramref name="edit"/> and makes it the script of later runs.</summary>
    internal void WriteScript(Func<string, string> edit)
    {
        string path = Path.Combine(Path.GetTempPath(), "voiceoff-script-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, edit(File.ReadAllText(ScriptPath)));
        scriptCopies.Add(path);
        ScriptPath = path;
    }

    private (string Engines, string Cards)? written;

    internal string EnginesDirectory => (written ??= (engines.Create(engineFiles.ToArray()), cards.Create(cardFiles.ToArray()))).Engines;

    internal string CardsDirectory => (written ??= (engines.Create(engineFiles.ToArray()), cards.Create(cardFiles.ToArray()))).Cards;

    /// <summary>Runs <c>render --run &lt;runId&gt;</c> with the standard options plus <paramref name="extra"/>; every container is running.</summary>
    internal RenderRun Run(string runId, params string[] extra) =>
        RunWith(new FakeDockerInspector(new ContainerState(true, true, "10.0.0.2")), CancellationToken.None, runId, extra);

    internal RenderRun RunWith(FakeDockerInspector docker, CancellationToken ct, string runId, params string[] extra) =>
        RunAt(Root, docker, ct, runId, extra);

    /// <summary>Like <see cref="RunWith"/> with <c>--root</c> pointing at <paramref name="root"/>.</summary>
    internal RenderRun RunAt(string root, FakeDockerInspector docker, CancellationToken ct, string runId, params string[] extra)
    {
        using HttpClient http = SpeechClient.CreateHttpClient();
        RenderServices services = new(docker, http, new FixedClockTimeProvider(new DateTimeOffset(2026, 10, 22, 19, 30, 0, TimeSpan.Zero)), new LoudnessMatch());
        string[] args = ["render", "--run", runId, "--root", root, "--engines", EnginesDirectory, "--cards", CardsDirectory, "--script", ScriptPath, .. extra];
        StringWriter output = new();
        StringWriter error = new();
        int exit = Cli.Run(args, output, error, services, ct);
        string runDirectory = Path.Combine(root, runId);
        return new RenderRun(exit, output.ToString(), error.ToString(), Directory.Exists(runDirectory) ? runDirectory : null);
    }

    public void Dispose()
    {
        foreach (EngineStubServer stub in stubs)
        {
            stub.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        engines.Dispose();
        cards.Dispose();
        foreach (string copy in scriptCopies)
        {
            File.Delete(copy);
        }

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
