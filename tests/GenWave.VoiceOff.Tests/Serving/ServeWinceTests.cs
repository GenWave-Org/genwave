using System.Net;
using System.Text.Json;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Serving;

public static class ServeWinceTests
{
    private static ServeScenario Reeled() =>
        new(new PairFixtureOptions(Challengers: ["wrenfox", "oakmoth"], Tones: true), run => run.MustReel());

    public sealed class ScenarioARunWithoutReels : IDisposable
    {
        private readonly ServeScenario serve = new();
        private readonly string body;

        public ScenarioARunWithoutReels() => body = serve.GetText("wince");

        [Fact]
        public void ListsNoReels() => Assert.Equal(0, JsonDocument.Parse(body).RootElement.GetProperty("reels").GetArrayLength());

        [Fact]
        public void RefusesACount() =>
            Assert.Equal(HttpStatusCode.BadRequest, serve.PostWince(ServeScenario.WinceJson(new string('a', 32), 1)).StatusCode);

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioAFreshReelRun : IDisposable
    {
        private readonly ServeScenario serve = Reeled();
        private readonly JsonElement reels;

        public ScenarioAFreshReelRun() => reels = JsonDocument.Parse(serve.GetText("wince")).RootElement.GetProperty("reels").Clone();

        [Fact]
        public void ListsThreeReels() => Assert.Equal(3, reels.GetArrayLength());

        [Fact]
        public void ListsThemInManifestOrder() =>
            Assert.Equal(serve.Run.Reels().Select(r => r.ReelId), reels.EnumerateArray().Select(r => r.GetProperty("reelId").GetString()));

        [Fact]
        public void HasNoCountsYet() => Assert.DoesNotContain(reels.EnumerateArray(), r => r.TryGetProperty("count", out _));

        [Fact]
        public void ServesAReelAsAudio()
        {
            using HttpResponseMessage answer = serve.Get("wince/" + serve.Run.Reels()[0].ReelId);
            Assert.Equal("audio/wav", answer.Content.Headers.ContentType?.MediaType);
        }

        [Fact]
        public void ServesAReelWithRangeSupport()
        {
            using HttpRequestMessage request = new(HttpMethod.Get, "wince/" + serve.Run.Reels()[0].ReelId);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 99);
            using HttpClient client = new() { BaseAddress = new Uri($"http://127.0.0.1:{serve.Port}/") };
            using HttpResponseMessage answer = client.Send(request);
            Assert.Equal(HttpStatusCode.PartialContent, answer.StatusCode);
        }

        [Fact]
        public void ServesAReelWithoutLastModifiedSoFileTimesDoNotLeakTheBuildOrder()
        {
            using HttpResponseMessage answer = serve.Get("wince/" + serve.Run.Reels()[0].ReelId);
            Assert.Null(answer.Content.Headers.LastModified);
        }

        [Fact]
        public void AnUnknownReelIs404() => Assert.Equal(HttpStatusCode.NotFound, serve.Get("wince/" + new string('a', 32)).StatusCode);

        [Fact]
        public void APathLikeIdIs404() => Assert.Equal(HttpStatusCode.NotFound, serve.Get("wince/..%2Fsessions.json").StatusCode);

        [Fact]
        public void TheWinceLogIsNotServed() => Assert.Equal(HttpStatusCode.NotFound, serve.Get("winces.jsonl").StatusCode);

        [Fact]
        public void TheReelsFolderIsNotServedByName() => Assert.Equal(HttpStatusCode.NotFound, serve.Get("reels/" + serve.Run.Reels()[0].ReelId + ".wav").StatusCode);

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioACountIsEntered : IDisposable
    {
        private readonly ServeScenario serve = Reeled();
        private readonly string reelId;
        private readonly HttpResponseMessage answer;

        public ScenarioACountIsEntered()
        {
            reelId = serve.Run.Reels()[0].ReelId;
            answer = serve.PostWince(ServeScenario.WinceJson(reelId, 4));
        }

        [Fact]
        public void Answers204() => Assert.Equal(HttpStatusCode.NoContent, answer.StatusCode);

        [Fact]
        public void IsAppendedToTheLog() => Assert.Single(serve.WinceLines());

        [Fact]
        public void IsStampedWithTheServerClock() => Assert.Contains("2026-10-22T12:00:00", serve.WinceLines()[0], StringComparison.Ordinal);

        [Fact]
        public void ShowsAsTheCount() =>
            Assert.Equal(4, JsonDocument.Parse(serve.GetText("wince")).RootElement.GetProperty("reels").EnumerateArray()
                .Single(r => r.GetProperty("reelId").GetString() == reelId).GetProperty("count").GetInt32());

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioACountIsEnteredTwice : IDisposable
    {
        private readonly ServeScenario serve = Reeled();
        private readonly string reelId;

        public ScenarioACountIsEnteredTwice()
        {
            reelId = serve.Run.Reels()[0].ReelId;
            serve.AcceptWince(reelId, 4);
            serve.AcceptWince(reelId, 0);
        }

        [Fact]
        public void BothLinesAreKept() => Assert.Equal(2, serve.WinceLines().Count);

        [Fact]
        public void TheLastOneCounts() =>
            Assert.Equal(0, JsonDocument.Parse(serve.GetText("wince")).RootElement.GetProperty("reels").EnumerateArray()
                .Single(r => r.GetProperty("reelId").GetString() == reelId).GetProperty("count").GetInt32());

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioRefusedCounts : IDisposable
    {
        private readonly ServeScenario serve = Reeled();
        private string ReelId => serve.Run.Reels()[0].ReelId;

        [Fact]
        public void ANegativeCountIs400() => Assert.Equal(HttpStatusCode.BadRequest, serve.PostWince(ServeScenario.WinceJson(ReelId, -1)).StatusCode);

        [Fact]
        public void ACountOf1000Is400() => Assert.Equal(HttpStatusCode.BadRequest, serve.PostWince(ServeScenario.WinceJson(ReelId, 1000)).StatusCode);

        [Fact]
        public void ACountOf999IsAccepted() => Assert.Equal(HttpStatusCode.NoContent, serve.PostWince(ServeScenario.WinceJson(ReelId, 999)).StatusCode);

        [Fact]
        public void AnUnknownReelIs400() => Assert.Equal(HttpStatusCode.BadRequest, serve.PostWince(ServeScenario.WinceJson(new string('a', 32), 1)).StatusCode);

        [Fact]
        public void AnExtraMemberIs400() =>
            Assert.Equal(HttpStatusCode.BadRequest, serve.PostWince($$"""{"reelId":"{{ReelId}}","count":1,"engine":"x"}""").StatusCode);

        [Fact]
        public void AFractionalCountIs400() =>
            Assert.Equal(HttpStatusCode.BadRequest, serve.PostWince($$"""{"reelId":"{{ReelId}}","count":1.5}""").StatusCode);

        [Fact]
        public void NotJsonIs415() => Assert.Equal(HttpStatusCode.UnsupportedMediaType, serve.PostWince(ServeScenario.WinceJson(ReelId, 1), "text/plain").StatusCode);

        [Fact]
        public void AnOversizeBodyIs413() =>
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, serve.PostWince($$"""{"reelId":"{{ReelId}}","count":1,"pad":"{{new string('x', 5000)}}"}""").StatusCode);

        [Fact]
        public void NothingIsLoggedForARefusal()
        {
            serve.PostWince(ServeScenario.WinceJson(ReelId, -1)).Dispose();
            Assert.Empty(serve.WinceLines());
        }

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioAManifestReelIdThatIsNot32Hex : IDisposable
    {
        private readonly PairScenario run = new();

        public ScenarioAManifestReelIdThatIsNot32Hex()
        {
            Assert.Equal(0, run.Pair());
            File.WriteAllText(
                Path.Combine(run.RunDirectory, "manifest.json"),
                GenWave.VoiceOff.Listening.StrictJson.Write(run.Manifest() with { Reels = [new GenWave.VoiceOff.Listening.ManifestReel("../../../etc/passwd", "wrenfox")] }));
        }

        [Fact]
        public void ServeRefusesToOpenTheRun() =>
            Assert.Throws<InvalidDataException>(() => GenWave.VoiceOff.Serving.ServeRun.Open(run.Root, run.RunId));

        public void Dispose() => run.Dispose();
    }
}
