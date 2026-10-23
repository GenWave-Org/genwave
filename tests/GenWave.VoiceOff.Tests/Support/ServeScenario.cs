using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Serving;
using Microsoft.AspNetCore.Builder;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>
/// A paired run (the <see cref="PairScenario"/> helpers, 40 pairs in session 1) served in-process on a free loopback port by the real <see cref="ServeHost"/>.
/// The shown address comes from a fixed interface list, so what it prints does not depend on the machine.
/// </summary>
internal sealed class ServeScenario : IDisposable
{
    internal const string LanIp = "192.168.1.20";
    internal static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-22T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    private readonly PairScenario run;
    private readonly WebApplication app;
    private readonly HttpClient http;
    private readonly StringWriter output = new();

    internal ServeScenario(PairFixtureOptions? fixture = null)
    {
        run = new PairScenario(fixture);
        if (run.Pair() != 0)
        {
            throw new InvalidOperationException("The fixture run did not pair: " + run.Stderr);
        }

        ServeOptions options = new(run.RunId, run.Root, 0, IPAddress.Any);
        NetworkCandidate[] candidates = [new("eth0", NetworkInterfaceType.Ethernet, OperationalStatus.Up, IPAddress.Parse(LanIp))];
        app = ServeHost.Build(options, new FixedClockTimeProvider(Now), output, candidates);
        app.StartAsync().GetAwaiter().GetResult();
        Port = ServeHost.BoundPort(app);
        http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{Port}/") };
    }

    internal int Port { get; }

    internal string EnvironmentName => app.Environment.EnvironmentName;

    internal string Stdout => output.ToString();

    internal PairScenario Run => run;

    internal string VotesPath => Path.Combine(run.RunDirectory, "votes.jsonl");

    /// <summary>Every byte of votes.jsonl, or nothing when no vote was ever accepted.</summary>
    internal byte[] VotesBytes() => File.Exists(VotesPath) ? File.ReadAllBytes(VotesPath) : [];

    /// <summary>The pair ids appearing in the vote log, whoever voted.</summary>
    internal IReadOnlySet<string> VotedPairIds()
    {
        HashSet<string> ids = [];
        foreach (string line in VoteLines())
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            ids.Add(doc.RootElement.GetProperty("pairId").GetString() ?? string.Empty);
        }
        return ids;
    }

    internal IReadOnlyList<string> VoteLines() => File.Exists(VotesPath) ? File.ReadAllLines(VotesPath) : [];

    /// <summary>Session 1's pair ids in the order pair wrote them.</summary>
    internal IReadOnlyList<string> PairIds() => [.. run.Sessions().Sessions[0].Pairs.Select(p => p.PairId)];

    internal HttpResponseMessage Get(string path) => http.GetAsync(path).GetAwaiter().GetResult();

    internal string GetText(string path) => Get(path).Content.ReadAsStringAsync().GetAwaiter().GetResult();

    internal int RawStatus(string path) => RawHttp.GetStatus(Port, path);

    internal HttpResponseMessage Post(string body, string contentType = "application/json")
    {
        StringContent content = new(body, Encoding.UTF8);
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        return Send(content);
    }

    internal HttpResponseMessage Send(HttpContent content) => http.PostAsync("vote", content).GetAwaiter().GetResult();

    internal HttpResponseMessage Vote(string pairId, string judge, string pick, string? why = null) => Post(VoteJson(pairId, judge, pick, why));

    /// <summary>Casts a vote that must be accepted; a refused setup vote would make a later "unchanged" check compare nothing to nothing.</summary>
    internal void AcceptVote(string pairId, string judge, string pick)
    {
        using HttpResponseMessage answer = Vote(pairId, judge, pick);
        if (answer.StatusCode != HttpStatusCode.NoContent)
        {
            throw new InvalidOperationException($"The setup vote was refused with {(int)answer.StatusCode}; the scenario would compare nothing to nothing.");
        }
    }

    internal static string VoteJson(string pairId, string judge, string pick, string? why = null) => JsonSerializer.Serialize(
        why is null
            ? new Dictionary<string, string> { ["pairId"] = pairId, ["judge"] = judge, ["pick"] = pick }
            : new Dictionary<string, string> { ["pairId"] = pairId, ["judge"] = judge, ["pick"] = pick, ["why"] = why });

    /// <summary>The pair ids <c>/session</c> answers a judge, in that judge's order.</summary>
    internal IReadOnlyList<string> SessionOrder(string judge)
    {
        using JsonDocument doc = JsonDocument.Parse(GetText($"session?s=1&judge={Uri.EscapeDataString(judge)}"));
        return [.. doc.RootElement.GetProperty("pairs").EnumerateArray().Select(p => p.GetProperty("pairId").GetString() ?? string.Empty)];
    }

    internal IReadOnlyDictionary<string, string> Progress(string judge)
    {
        using JsonDocument doc = JsonDocument.Parse(GetText($"progress?s=1&judge={Uri.EscapeDataString(judge)}"));
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString(), StringComparer.Ordinal);
    }

    public void Dispose()
    {
        http.Dispose();
        app.StopAsync().GetAwaiter().GetResult();
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        run.Dispose();
    }
}
