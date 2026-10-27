using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json.Nodes;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Serving;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>
/// A rendered run served in-process on a free port by the real <see cref="ServeHost"/>: three cards (<see cref="First"/> and <see cref="Second"/> rendered,
/// <see cref="Third"/> failed), their card files, one engine profile and a clip for each rendered card, all in a temp folder.
/// The shown address comes from a fixed interface list, so what it prints does not depend on the machine.
/// </summary>
internal sealed class ServeScenario : IDisposable
{
    internal const string First = "aaa-first";
    internal const string Second = "bbb-second";
    internal const string Third = "ccc-third";
    internal const string Gone = "ddd-gone";
    internal const string FailureReason = "container was down";
    internal const string LanIp = "192.168.1.20";
    internal static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-27T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    private readonly string root = Path.Combine(Path.GetTempPath(), "voiceoff-serve-" + Guid.NewGuid().ToString("N"));
    private readonly EngineDirectoryFixture engines = new();
    private readonly CardFixtureDirectory cards = new();
    private readonly WebApplication app;
    private readonly HttpClient http;
    private readonly StringWriter output = new();
    private readonly StringWriter errorOutput = new();

    /// <param name="withGoneCard">Adds a fourth card to run.json that has no card file, as when a card is deleted after the render.</param>
    /// <param name="bind">The address to listen on; loopback by default.</param>
    /// <param name="beforeStart">Runs with the run folder before the server starts, e.g. to drop a config file into it.</param>
    internal ServeScenario(bool withGoneCard = false, IPAddress? bind = null, Action<string>? beforeStart = null)
    {
        RunId = "served";
        RunDirectory = Path.Combine(root, RunId);
        Directory.CreateDirectory(Path.Combine(RunDirectory, "cards"));
        FirstClip = WavBytes.Make(24000, 4800);
        File.WriteAllBytes(Path.Combine(RunDirectory, "cards", First + ".wav"), FirstClip);
        File.WriteAllBytes(Path.Combine(RunDirectory, "cards", Second + ".wav"), WavBytes.Make(24000, 9600));

        Dictionary<string, CardEntry> runCards = new(StringComparer.Ordinal)
        {
            [First] = new CardEntry("thor-cpu", true),
            [Second] = new CardEntry("thor-cpu", true),
            [Third] = new CardEntry("thor-gpu", false, FailureReason),
        };
        if (withGoneCard)
        {
            runCards[Gone] = new CardEntry("runes-gpu", true);
            File.WriteAllBytes(Path.Combine(RunDirectory, "cards", Gone + ".wav"), WavBytes.Make(24000, 2400));
        }

        new RunManifest(RunId, "hash", Now, runCards).WriteAsync(Path.Combine(RunDirectory, "run.json"), CancellationToken.None).GetAwaiter().GetResult();

        string enginesDirectory = engines.Create(("alpha.json", EngineProfileJson.Valid("alpha").ToJsonString()));
        string cardsDirectory = cards.Create(
            (First + ".json", CardFixtureDirectory.CardJson(First, "alpha", extraMembers: "\"asShipped\": true,")),
            (Second + ".json", CardFixtureDirectory.CardJson(Second, "alpha")),
            (Third + ".json", CardFixtureDirectory.CardJson(Third, "alpha")));

        Options = new ServeOptions(RunId, root, 0, bind ?? IPAddress.Loopback, enginesDirectory, cardsDirectory);
        NetworkCandidate[] candidates = [new("eth0", NetworkInterfaceType.Ethernet, OperationalStatus.Up, IPAddress.Parse(LanIp))];
        beforeStart?.Invoke(RunDirectory);
        app = ServeHost.BuildAsync(Options, new FixedClockTimeProvider(Now), output, errorOutput, CancellationToken.None, candidates).GetAwaiter().GetResult();
        app.StartAsync().GetAwaiter().GetResult();
        Port = ServeHost.BoundPort(app);
        http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{Port}/") };
    }

    internal string RunId { get; }

    internal string RunDirectory { get; }

    internal ServeOptions Options { get; }

    internal int Port { get; }

    internal byte[] FirstClip { get; }

    /// <summary>Every address the server is listening on.</summary>
    internal IReadOnlyList<Uri> BoundUrls =>
        [.. (app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses ?? []).Select(a => new Uri(a))];

    internal string EnvironmentName => app.Environment.EnvironmentName;

    internal string Stdout => output.ToString();

    /// <summary>What the server wrote to its error stream, e.g. the line for a failed request.</summary>
    internal string Stderr => errorOutput.ToString();

    internal string JudgementPath => Path.Combine(RunDirectory, "judgement.json");

    /// <summary>Every byte of judgement.json, or nothing when none was ever accepted.</summary>
    internal byte[] JudgementBytes() => File.Exists(JudgementPath) ? File.ReadAllBytes(JudgementPath) : [];

    internal HttpResponseMessage Get(string path) => http.GetAsync(path).GetAwaiter().GetResult();

    internal string GetText(string path) => Get(path).Content.ReadAsStringAsync().GetAwaiter().GetResult();

    internal int RawStatus(string path) => RawHttp.GetStatus(Port, path);

    internal HttpResponseMessage Put(string body, string contentType = "application/json")
    {
        StringContent content = new(body, Encoding.UTF8);
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        return Send(content);
    }

    internal HttpResponseMessage Send(HttpContent content) => http.PutAsync("judgement", content).GetAwaiter().GetResult();

    /// <summary>A whole-document body: the order and one entry per card given as (id, winces, stars, notes).</summary>
    internal static string Judgement(string[] order, params (string Id, int Winces, int? Stars, string Notes)[] cards)
    {
        JsonObject entries = [];
        foreach ((string id, int winces, int? stars, string notes) in cards)
        {
            entries[id] = new JsonObject { ["winces"] = winces, ["stars"] = stars, ["notes"] = notes };
        }

        return new JsonObject { ["order"] = new JsonArray([.. order.Select(o => JsonValue.Create(o))]), ["cards"] = entries }.ToJsonString();
    }

    /// <summary>Saves a judgement that must be accepted; a refused setup would make a later "unchanged" check compare nothing to nothing.</summary>
    internal void Accept(string body)
    {
        using HttpResponseMessage answer = Put(body);
        if (answer.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"The setup judgement was refused with {(int)answer.StatusCode}.");
        }
    }

    public void Dispose()
    {
        http.Dispose();
        app.StopAsync().GetAwaiter().GetResult();
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        engines.Dispose();
        cards.Dispose();
        Directory.Delete(root, recursive: true);
    }
}
