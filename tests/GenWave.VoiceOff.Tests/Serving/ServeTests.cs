using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using GenWave.VoiceOff.Serving;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Serving;

public static class ServeTests
{
    public sealed class ScenarioTheIndexPage : IDisposable
    {
        private readonly ServeScenario serve = new();
        private readonly HttpResponseMessage answer;

        public ScenarioTheIndexPage() => answer = serve.Get("");

        [Fact]
        public void Answers200() => Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        [Fact]
        public void IsHtml() => Assert.Equal("text/html", answer.Content.Headers.ContentType?.MediaType);

        [Fact]
        public void CarriesTheContentSecurityPolicy() =>
            Assert.Equal(SecurityHeaders.ContentSecurityPolicy, answer.Headers.GetValues("Content-Security-Policy").Single());

        [Fact]
        public void TheContentSecurityPolicyIsTheAgreedText() => Assert.Equal(
            "default-src 'self'; media-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'",
            SecurityHeaders.ContentSecurityPolicy);

        [Fact]
        public void CarriesNoSniff() => Assert.Equal("nosniff", answer.Headers.GetValues("X-Content-Type-Options").Single());

        [Fact]
        public void CarriesNoReferrer() => Assert.Equal("no-referrer", answer.Headers.GetValues("Referrer-Policy").Single());

        [Fact]
        public void CarriesNoStore() => Assert.Equal("no-store", answer.Headers.GetValues("Cache-Control").Single());

        [Fact]
        public void NamesNoServer() => Assert.False(answer.Headers.Contains("Server"));

        [Fact]
        public void StatesTheNameRule() =>
            Assert.Contains("Use up to 32 letters, numbers, spaces, hyphens or underscores.", serve.GetText(""), StringComparison.Ordinal);

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioAnErrorAnswerStillCarriesTheHeaders : IDisposable
    {
        private readonly ServeScenario serve = new();
        private readonly HttpResponseMessage answer;

        public ScenarioAnErrorAnswerStillCarriesTheHeaders() => answer = serve.Get("nothing-here");

        [Fact]
        public void Is404() => Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);

        [Fact]
        public void HasNoStore() => Assert.Equal("no-store", answer.Headers.GetValues("Cache-Control").Single());

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioAClip : IDisposable
    {
        private readonly ServeScenario serve = new();
        private readonly HttpResponseMessage answer;
        private readonly string clipUrl;

        public ScenarioAClip()
        {
            clipUrl = serve.Run.Sessions().Sessions[0].Pairs[0].A;
            answer = serve.Get(clipUrl);
        }

        [Fact]
        public void Answers200() => Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        [Fact]
        public void IsAudioWav() => Assert.Equal("audio/wav", answer.Content.Headers.ContentType?.MediaType);

        [Fact]
        public async Task IsTheBytesOnDisk() => Assert.Equal(
            await File.ReadAllBytesAsync(Path.Combine(serve.Run.SessionDirectory, clipUrl)),
            await answer.Content.ReadAsByteArrayAsync());

        [Fact]
        public async Task AnswersARangeRequest()
        {
            using HttpRequestMessage request = new(HttpMethod.Get, clipUrl);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 9);
            using HttpClient client = new() { BaseAddress = new Uri($"http://127.0.0.1:{serve.Port}/") };
            Assert.Equal(HttpStatusCode.PartialContent, (await client.SendAsync(request)).StatusCode);
        }

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioPathsThatMustNotBeServed : IDisposable
    {
        private readonly ServeScenario serve = new();

        [Theory]
        [InlineData("/sessions.json")]
        [InlineData("/index.html")]
        [InlineData("/votes.jsonl")]
        [InlineData("/clips/")]
        [InlineData("/clips")]
        [InlineData("/clips/00000000000000000000000000000000.wav")]
        [InlineData("/clips/../sessions.json")]
        [InlineData("/%2e%2e/manifest.json")]
        [InlineData("/clips/..%2f..%2fmanifest.json")]
        [InlineData("/clips/%2e%2e/%2e%2e/manifest.json")]
        [InlineData("/%2e%2e%2fmanifest.json")]
        [InlineData("/winces.jsonl")]
        public void Answer404(string path) => Assert.Equal(404, serve.RawStatus(path));

        [Fact]
        public void AClipNameThatIsNotListedAnswers404()
        {
            string unlisted = new('a', 32);
            Assert.Equal(HttpStatusCode.NotFound, serve.Get($"clips/{unlisted}.wav").StatusCode);
        }

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioSessionRefusals : IDisposable
    {
        private readonly ServeScenario serve = new();

        [Fact]
        public void AnUnknownSessionAnswers404() => Assert.Equal(HttpStatusCode.NotFound, serve.Get("session?s=99&judge=Dean").StatusCode);

        [Fact]
        public void ANoSessionAnswers404() => Assert.Equal(HttpStatusCode.NotFound, serve.Get("session?judge=Dean").StatusCode);

        [Fact]
        public void ABadJudgeAnswers400() => Assert.Equal(HttpStatusCode.BadRequest, serve.Get("session?s=1&judge=%3Cscript%3E").StatusCode);

        [Fact]
        public void ATrailingNewlineJudgeAnswers400() => Assert.Equal(HttpStatusCode.BadRequest, serve.Get("session?s=1&judge=Dean%0A").StatusCode);

        [Fact]
        public void ANoJudgeAnswers400() => Assert.Equal(HttpStatusCode.BadRequest, serve.Get("session?s=1").StatusCode);

        [Fact]
        public void AnAllSpaceJudgeAnswers400() => Assert.Equal(HttpStatusCode.BadRequest, serve.Get("session?s=1&judge=%20%20").StatusCode);

        [Fact]
        public void A33CharacterJudgeAnswers400() => Assert.Equal(HttpStatusCode.BadRequest, serve.Get($"session?s=1&judge={new string('a', 33)}").StatusCode);

        [Fact]
        public void A32CharacterJudgeAnswers200() => Assert.Equal(HttpStatusCode.OK, serve.Get($"session?s=1&judge={new string('a', 32)}").StatusCode);

        [Fact]
        public void ProgressForAnUnknownSessionAnswers404() => Assert.Equal(HttpStatusCode.NotFound, serve.Get("progress?s=99&judge=Dean").StatusCode);

        [Fact]
        public void ProgressForABadJudgeAnswers400() => Assert.Equal(HttpStatusCode.BadRequest, serve.Get("progress?s=1&judge=%3Cb%3E").StatusCode);

        [Fact]
        public void AnotherMethodOnSessionAnswers405() => Assert.Equal(HttpStatusCode.MethodNotAllowed, PostSession());

        private HttpStatusCode PostSession()
        {
            using HttpClient client = new() { BaseAddress = new Uri($"http://127.0.0.1:{serve.Port}/") };
            return client.PostAsync("session?s=1&judge=Dean", new StringContent("{}")).GetAwaiter().GetResult().StatusCode;
        }

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioVoteRefusals : IDisposable
    {
        private readonly ServeScenario serve = new();
        private readonly string pairId;

        public ScenarioVoteRefusals() => pairId = serve.PairIds()[0];

        private HttpStatusCode Status(string body, string contentType = "application/json") => serve.Post(body, contentType).StatusCode;

        [Fact]
        public void ATextPlainBodyAnswers415() =>
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, Status(ServeScenario.VoteJson(pairId, "Dean", "a"), "text/plain"));

        [Fact]
        public void ACharsetOnJsonIsFine() =>
            Assert.Equal(HttpStatusCode.NoContent, Status(ServeScenario.VoteJson(pairId, "Dean", "a"), "application/json; charset=utf-8"));

        [Fact]
        public void AnUnknownMemberAnswers400() =>
            Assert.Equal(HttpStatusCode.BadRequest, Status($$"""{"pairId":"{{pairId}}","judge":"Dean","pick":"a","extra":1}"""));

        [Fact]
        public void AnUnknownPairAnswers400() =>
            Assert.Equal(HttpStatusCode.BadRequest, Status(ServeScenario.VoteJson(new string('0', 32), "Dean", "a")));

        [Fact]
        public void AnUnknownPickAnswers400() => Assert.Equal(HttpStatusCode.BadRequest, Status(ServeScenario.VoteJson(pairId, "Dean", "c")));

        [Fact]
        public void ANumericPickAnswers400() =>
            Assert.Equal(HttpStatusCode.BadRequest, Status($$"""{"pairId":"{{pairId}}","judge":"Dean","pick":0}"""));

        [Fact]
        public void AMissingJudgeAnswers400() => Assert.Equal(HttpStatusCode.BadRequest, Status($$"""{"pairId":"{{pairId}}","pick":"a"}"""));

        [Fact]
        public void ANullWhyIsTreatedAsNoWhy() =>
            Assert.Equal(HttpStatusCode.NoContent, Status($$"""{"pairId":"{{pairId}}","judge":"Dean","pick":"a","why":null}"""));

        [Fact]
        public void ADuplicateKeyAnswers400() =>
            Assert.Equal(HttpStatusCode.BadRequest, Status($$"""{"pairId":"{{pairId}}","judge":"Dean","judge":"Alex","pick":"a"}"""));

        [Fact]
        public void AnEmptyBodyAnswers400() => Assert.Equal(HttpStatusCode.BadRequest, Status(string.Empty));

        [Fact]
        public void NotJsonAnswers400() => Assert.Equal(HttpStatusCode.BadRequest, Status("vote for a"));

        [Fact]
        public void ATrailingNewlineJudgeAnswers400() =>
            Assert.Equal(HttpStatusCode.BadRequest, Status(ServeScenario.VoteJson(pairId, "Dean\n", "a")));

        [Fact]
        public void ABodyOfExactly4096BytesIsRead()
        {
            string padded = ServeScenario.VoteJson(pairId, "Dean", "a");
            padded += new string(' ', 4096 - Encoding.UTF8.GetByteCount(padded));
            Assert.Equal(HttpStatusCode.NoContent, Status(padded));
        }

        [Fact]
        public void ABodyOf4097BytesAnswers413()
        {
            string padded = ServeScenario.VoteJson(pairId, "Dean", "a");
            padded += new string(' ', 4097 - Encoding.UTF8.GetByteCount(padded));
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, Status(padded));
        }

        [Fact]
        public void AChunkedBodyWithoutContentLengthOf5KBAnswers413()
        {
            byte[] big = Encoding.UTF8.GetBytes(ServeScenario.VoteJson(pairId, "Dean", "a") + new string(' ', 5 * 1024));
            using UnknownLengthContent content = new(big);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, serve.Send(content).StatusCode);
        }

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioRefusalsAfterAnAcceptedVote : IDisposable
    {
        private readonly ServeScenario serve = new();
        private readonly byte[] before;

        public ScenarioRefusalsAfterAnAcceptedVote()
        {
            string pairId = serve.PairIds()[0];
            serve.Vote(pairId, "Dean", "a").Dispose();
            before = serve.VotesBytes();
            serve.Post("vote for a").Dispose();
            serve.Post(ServeScenario.VoteJson(pairId, "Dean", "b"), "text/plain").Dispose();
            serve.Post(ServeScenario.VoteJson(new string('0', 32), "Dean", "a")).Dispose();
            serve.Post(ServeScenario.VoteJson(pairId, "Dean", "b", "loud")).Dispose();
        }

        [Fact]
        public void TheFileHoldsTheAcceptedVote() => Assert.NotEmpty(before);

        [Fact]
        public void VotesJsonlIsByteIdentical() => Assert.Equal(before, serve.VotesBytes());

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioEveryWhyTagIsAccepted : IDisposable
    {
        private readonly ServeScenario serve = new();

        [Theory]
        [InlineData("robotic")]
        [InlineData("pace")]
        [InlineData("pronunciation")]
        [InlineData("flat")]
        [InlineData("breathy")]
        [InlineData("glitch")]
        [InlineData("other")]
        public void Answers204(string why) => Assert.Equal(HttpStatusCode.NoContent, serve.Vote(serve.PairIds()[0], "Dean", "tie", why).StatusCode);

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioTwoJudgesAndSpaces : IDisposable
    {
        private readonly ServeScenario serve = new();

        public ScenarioTwoJudgesAndSpaces()
        {
            string pair = serve.PairIds()[0];
            serve.Vote(pair, "Dean", "a");
            serve.Vote(pair, " Dean ", "b");
            serve.Vote(pair, "Alex", "a");
        }

        [Fact]
        public void EdgeSpacesMakeNoNewJudge() => Assert.Equal("1", serve.Progress("Dean")["done"]);

        [Fact]
        public void AnotherJudgeStartsAtZero() => Assert.Equal("0", serve.Progress("Sam")["done"]);

        [Fact]
        public void TheStoredJudgeIsTrimmed() => Assert.DoesNotContain(" Dean", string.Join('\n', serve.VoteLines()), StringComparison.Ordinal);

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioAVoteServeWroteIsScored : IDisposable
    {
        private readonly ServeScenario serve = new();
        private readonly int exit;

        public ScenarioAVoteServeWroteIsScored()
        {
            serve.Vote(serve.PairIds()[0], "Dean", "a", "pace");
            exit = serve.Run.Run(["score", "--run", serve.Run.RunId, "--root", serve.Run.Root, "--engines", serve.Run.EnginesDirectory, "--out", Path.Combine(serve.Run.Root, "RESULTS.md")]);
        }

        [Fact]
        public void ScoreExitsZero() => Assert.Equal(0, exit);

        [Fact]
        public void ScoreSawTheJudge() => Assert.Contains("Dean", File.ReadAllText(Path.Combine(serve.Run.Root, "RESULTS.md")), StringComparison.Ordinal);

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioTheVerb : IDisposable
    {
        private readonly PairScenario fixture = new();

        [Fact]
        public void NoRunIsAUsageError() => Assert.Equal(2, fixture.Run(["serve"]));

        [Fact]
        public void AnUnknownOptionIsAUsageError() => Assert.Equal(2, fixture.Run(["serve", "--run", fixture.RunId, "--bogus", "1"]));

        [Fact]
        public void ABadPortIsAUsageError() => Assert.Equal(2, fixture.Run(["serve", "--run", fixture.RunId, "--port", "70000"]));

        [Fact]
        public void ABadBindIsAUsageError() => Assert.Equal(2, fixture.Run(["serve", "--run", fixture.RunId, "--bind", "nowhere"]));

        [Fact]
        public void AMissingRunExits1() =>
            Assert.Equal(1, fixture.Run(["serve", "--run", "20250101T000000Z-cpu", "--root", fixture.Root, "--port", "0"]));

        [Fact]
        public void AnUnpairedRunExits1() =>
            Assert.Equal(1, fixture.Run(["serve", "--run", fixture.RunId, "--root", fixture.Root, "--port", "0"]));

        [Fact]
        public void AnUnpairedRunSaysToPairFirst()
        {
            fixture.Run(["serve", "--run", fixture.RunId, "--root", fixture.Root, "--port", "0"]);
            Assert.Contains("voiceoff pair", fixture.Stderr, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ACancelledRunStopsCleanlyWithExit0()
        {
            Assert.Equal(0, fixture.Pair());
            using CancellationTokenSource cancel = new();
            StringWriter output = new();
            Task<int> serving = Cli.RunAsync(
                ["serve", "--run", fixture.RunId, "--root", fixture.Root, "--port", "0", "--bind", "127.0.0.1"],
                output,
                new StringWriter(),
                GenWave.VoiceOff.Render.RenderServices.Default(),
                cancel.Token);
            SpinWait.SpinUntil(() => output.ToString().Contains("Open http://127.0.0.1:", StringComparison.Ordinal), TimeSpan.FromSeconds(20));
            cancel.Cancel();
            Assert.Equal(0, await serving);
        }

        public void Dispose() => fixture.Dispose();
    }

    public sealed class ScenarioTheShownAddress
    {
        private static NetworkCandidate Nic(string name, string ip, OperationalStatus status = OperationalStatus.Up, NetworkInterfaceType type = NetworkInterfaceType.Ethernet) =>
            new(name, type, status, IPAddress.Parse(ip));

        [Fact]
        public void TheFirstUpInterfaceWins() =>
            Assert.Equal("10.0.0.5", LanAddress.Choose([Nic("eth0", "10.0.0.5"), Nic("wlan0", "10.0.0.6")]).Address.ToString());

        [Fact]
        public void ContainerBridgesComeAfterRealInterfaces() =>
            Assert.Equal("192.168.1.9", LanAddress.Choose([Nic("docker0", "172.17.0.1"), Nic("br-1a2b", "172.18.0.1"), Nic("wlan0", "192.168.1.9")]).Address.ToString());

        [Theory]
        [InlineData("docker0")]
        [InlineData("br-1a2b3c")]
        [InlineData("veth12ab")]
        [InlineData("virbr0")]
        public void AVirtualOnlyMachineStillGetsItsAddress(string name) =>
            Assert.Equal("172.17.0.1", LanAddress.Choose([Nic(name, "172.17.0.1")]).Address.ToString());

        [Fact]
        public void ADownInterfaceIsSkipped() =>
            Assert.Equal("10.0.0.6", LanAddress.Choose([Nic("eth0", "10.0.0.5", OperationalStatus.Down), Nic("wlan0", "10.0.0.6")]).Address.ToString());

        [Fact]
        public void TheLoopbackIsSkipped() =>
            Assert.Equal("10.0.0.6", LanAddress.Choose([Nic("lo", "127.0.0.1", type: NetworkInterfaceType.Loopback), Nic("wlan0", "10.0.0.6")]).Address.ToString());

        [Fact]
        public void ALinkLocalAddressIsSkipped() =>
            Assert.Equal("10.0.0.6", LanAddress.Choose([Nic("eth0", "169.254.3.4"), Nic("wlan0", "10.0.0.6")]).Address.ToString());

        [Fact]
        public void ATunnelIsSkipped() =>
            Assert.Equal("10.0.0.6", LanAddress.Choose([Nic("tun0", "10.8.0.2", type: NetworkInterfaceType.Tunnel), Nic("wlan0", "10.0.0.6")]).Address.ToString());

        [Fact]
        public void NothingUsableFallsBackToTheLoopback() => Assert.Equal("127.0.0.1", LanAddress.Choose([]).Address.ToString());

        [Fact]
        public void TheFallbackIsFlagged() => Assert.True(LanAddress.Choose([]).IsFallback);

        [Fact]
        public void AFoundAddressIsNotAFallback() => Assert.False(LanAddress.Choose([Nic("eth0", "10.0.0.5")]).IsFallback);
    }

    public sealed class ScenarioTheShuffleSeed
    {
        [Fact]
        public void IsStableAcrossCalls() => Assert.Equal(SessionShuffle.Seed("Dean", "1"), SessionShuffle.Seed("Dean", "1"));

        [Fact]
        public void IsAPinnedValue() => Assert.Equal(1807029740, SessionShuffle.Seed("Dean", "1"));

        [Fact]
        public void DiffersByJudge() => Assert.NotEqual(SessionShuffle.Seed("Dean", "1"), SessionShuffle.Seed("Alex", "1"));

        [Fact]
        public void DiffersBySession() => Assert.NotEqual(SessionShuffle.Seed("Dean", "1"), SessionShuffle.Seed("Dean", "2"));
    }
}
