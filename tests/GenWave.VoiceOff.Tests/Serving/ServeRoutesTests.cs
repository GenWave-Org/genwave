using System.Net;
using System.Text.Json.Nodes;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Serving;

public static class ServeRoutesTests
{
    private static JsonNode Parse(string json) =>
        JsonNode.Parse(json) ?? throw new InvalidOperationException("The reply was JSON null.");

    public sealed class ScenarioThePage : IDisposable
    {
        private readonly ServeScenario serve = new();
        private readonly HttpResponseMessage answer;

        public ScenarioThePage() => answer = serve.Get("");

        [Fact]
        public void Answers200() => Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        [Fact]
        public void IsHtml() => Assert.Equal("text/html", answer.Content.Headers.ContentType?.MediaType);

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioStateBeforeAnyJudgement : IDisposable
    {
        private readonly ServeScenario serve = new();
        private readonly JsonNode state;

        public ScenarioStateBeforeAnyJudgement() => state = Parse(serve.GetText("state"));

        [Fact]
        public void TheJudgementIsNull() => Assert.Null(state["judgement"]);

        [Fact]
        public void TheCardsAreInIdOrder() =>
            Assert.Equal([ServeScenario.First, ServeScenario.Second, ServeScenario.Third], state["cards"]?.AsArray().Select(c => c?["id"]?.GetValue<string>()));

        [Fact]
        public void TheEngineNameComesFromTheState() => Assert.Equal("alpha", state["cards"]?[0]?["details"]?["engine"]?.GetValue<string>());

        [Fact]
        public void TheRunIdIsNamed() => Assert.Equal(serve.RunId, state["runId"]?.GetValue<string>());

        [Fact]
        public void TheFailedCardIsNotRendered() => Assert.False(state["cards"]?[2]?["rendered"]?.GetValue<bool>());

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioACardWhoseFileIsGone : IDisposable
    {
        private readonly ServeScenario serve = new(withGoneCard: true);
        private readonly JsonNode state;

        public ScenarioACardWhoseFileIsGone() => state = Parse(serve.GetText("state"));

        [Fact]
        public void TheCardIsStillListed() => Assert.Equal(4, state["cards"]?.AsArray().Count);

        [Fact]
        public void ItHasNoDetails() => Assert.Null(state["cards"]?[3]?["details"]);

        [Fact]
        public void ItsClipIsStillServed() => Assert.Equal(HttpStatusCode.OK, serve.Get($"clips/{ServeScenario.Gone}.wav").StatusCode);

        [Fact]
        public void ItCanStillBeJudged() =>
            Assert.Equal(HttpStatusCode.OK, serve.Put(ServeScenario.Judgement([ServeScenario.Gone], (ServeScenario.Gone, 1, 2, ""))).StatusCode);

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioClipsThatMustNotBeServed : IDisposable
    {
        private readonly ServeScenario serve = new();

        [Theory]
        [InlineData("/run.json")]
        [InlineData("/renders.jsonl")]
        [InlineData("/judgement.json")]
        [InlineData("/cards/aaa-first.wav")]
        [InlineData("/clips/")]
        [InlineData("/clips")]
        [InlineData("/clips/aaa-first")]
        [InlineData("/clips/AAA-FIRST.wav")]
        [InlineData("/clips/zzz-nothing.wav")]
        [InlineData("/clips/ccc-third.wav")]
        [InlineData("/clips/../run.json")]
        [InlineData("/%2e%2e/run.json")]
        [InlineData("/clips/..%2f..%2frun.json")]
        [InlineData("/clips/..%5c..%5crun.json")]
        [InlineData("/..%5crun.json")]
        [InlineData("/clips/%2e%2e/%2e%2e/run.json")]
        [InlineData("/clips/aaa-first.wav/../../run.json")]
        public void Answer404(string path) => Assert.Equal(404, serve.RawStatus(path));

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioAClipRangeRequest : IAsyncLifetime, IDisposable
    {
        private readonly ServeScenario serve = new();
        private HttpStatusCode status;

        public async Task InitializeAsync()
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"clips/{ServeScenario.First}.wav");
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 9);
            using HttpClient client = new() { BaseAddress = new Uri($"http://127.0.0.1:{serve.Port}/") };
            using HttpResponseMessage answer = await client.SendAsync(request);
            status = answer.StatusCode;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void AnswersPartialContent() => Assert.Equal(HttpStatusCode.PartialContent, status);

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioOtherMethods : IAsyncLifetime, IDisposable
    {
        private readonly ServeScenario serve = new();
        private HttpStatusCode postStatus;

        public async Task InitializeAsync()
        {
            using HttpClient client = new() { BaseAddress = new Uri($"http://127.0.0.1:{serve.Port}/") };
            using StringContent content = new("{}", System.Text.Encoding.UTF8, "application/json");
            using HttpResponseMessage answer = await client.PostAsync("judgement", content);
            postStatus = answer.StatusCode;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void GetOnJudgementAnswers405() => Assert.Equal(HttpStatusCode.MethodNotAllowed, serve.Get("judgement").StatusCode);

        [Fact]
        public void PostOnJudgementAnswers405() => Assert.Equal(HttpStatusCode.MethodNotAllowed, postStatus);

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioJudgementRefusals : IDisposable
    {
        private readonly ServeScenario serve = new();
        private readonly byte[] before;
        private readonly byte[] after;

        public ScenarioJudgementRefusals()
        {
            serve.Accept(ServeScenario.Judgement([ServeScenario.First], (ServeScenario.First, 1, 3, "kept")));
            before = serve.JudgementBytes();
            Status("""{"order":[],"cards":{},"judge":"Dean"}""");
            Status(ServeScenario.Judgement([], (ServeScenario.First, 0, 6, "")));
            Status("not json");
            Status(ServeScenario.Judgement([], ("zzz-nothing", 0, 3, "")), "text/plain");
            after = serve.JudgementBytes();
        }

        private HttpStatusCode Status(string body, string contentType = "application/json") => serve.Put(body, contentType).StatusCode;

        [Fact]
        public void AnUnknownMemberAnswers400() => Assert.Equal(HttpStatusCode.BadRequest, Status("""{"order":[],"cards":{},"judge":"Dean"}"""));

        [Fact]
        public void AnUnknownCardMemberAnswers400() =>
            Assert.Equal(HttpStatusCode.BadRequest, Status("""{"order":[],"cards":{"aaa-first":{"winces":0,"loud":true}}}"""));

        [Fact]
        public void ACardNotInTheRunInCardsAnswers400() =>
            Assert.Equal(HttpStatusCode.BadRequest, Status(ServeScenario.Judgement([], ("zzz-nothing", 0, 3, ""))));

        [Fact]
        public void ACardRepeatedInTheOrderAnswers400() =>
            Assert.Equal(HttpStatusCode.BadRequest, Status(ServeScenario.Judgement([ServeScenario.First, ServeScenario.First])));

        [Fact]
        public void ZeroStarsAnswers400() => Assert.Equal(HttpStatusCode.BadRequest, Status(ServeScenario.Judgement([], (ServeScenario.First, 0, 0, ""))));

        [Fact]
        public void WincesAsTextAnswers400() =>
            Assert.Equal(HttpStatusCode.BadRequest, Status("""{"order":[],"cards":{"aaa-first":{"winces":"2"}}}"""));

        [Fact]
        public void NotesOverTheLimitAnswer400() =>
            Assert.Equal(HttpStatusCode.BadRequest, Status(ServeScenario.Judgement([], (ServeScenario.First, 0, 3, new string('n', 2001)))));

        [Fact]
        public void ANullOrderEntryAnswers400() => Assert.Equal(HttpStatusCode.BadRequest, Status("""{"order":[null],"cards":{}}"""));

        [Fact]
        public void ANullCardEntryAnswers400() =>
            Assert.Equal(HttpStatusCode.BadRequest, Status("""{"order":[],"cards":{"aaa-first":null}}"""));

        [Fact]
        public void AWrongTypedBodyAnswers400() => Assert.Equal(HttpStatusCode.BadRequest, Status("""{"order":"x","cards":{}}"""));

        [Fact]
        public async Task AnUnknownMembersReplyNamesNoInternals()
        {
            using HttpResponseMessage answer = serve.Put("""{"order":[],"cards":{},"judge":"Dean"}""");
            Assert.DoesNotContain("GenWave", await answer.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task AWrongTypedBodysReplyNamesNoInternals()
        {
            using HttpResponseMessage answer = serve.Put("""{"order":"x","cards":{}}""");
            Assert.DoesNotContain("System.", await answer.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        [Fact]
        public void ADuplicateKeyAnswers400() => Assert.Equal(HttpStatusCode.BadRequest, Status("""{"order":[],"order":[],"cards":{}}"""));

        [Fact]
        public void AMissingOrderAnswers400() => Assert.Equal(HttpStatusCode.BadRequest, Status("""{"cards":{}}"""));

        [Fact]
        public void AnEmptyBodyAnswers400() => Assert.Equal(HttpStatusCode.BadRequest, Status(string.Empty));

        [Fact]
        public void JsonNullAnswers400() => Assert.Equal(HttpStatusCode.BadRequest, Status("null"));

        [Fact]
        public void ATextPlainBodyAnswers415() => Assert.Equal(HttpStatusCode.UnsupportedMediaType, Status(ServeScenario.Judgement([]), "text/plain"));

        [Fact]
        public void TheSampledRefusalsLeaveJudgementJsonByteIdentical() => Assert.Equal(before, after);

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioJudgementAcceptances : IDisposable
    {
        private readonly ServeScenario serve = new();
        private readonly DateTimeOffset? savedAt;

        public ScenarioJudgementAcceptances()
        {
            serve.Accept("""{"savedAt":"2001-01-01T00:00:00Z","order":[],"cards":{}}""");
            savedAt = Parse(File.ReadAllText(serve.JudgementPath))["savedAt"]?.GetValue<DateTimeOffset>();
        }

        [Fact]
        public void AnUnratedCardIsAccepted() =>
            Assert.Equal(HttpStatusCode.OK, serve.Put(ServeScenario.Judgement([], (ServeScenario.First, 2, null, ""))).StatusCode);

        [Fact]
        public void AnAbsentStarsMemberIsAccepted() =>
            Assert.Equal(HttpStatusCode.OK, serve.Put("""{"order":[],"cards":{"aaa-first":{"winces":1}}}""").StatusCode);

        [Fact]
        public void AnEmptyJudgementIsAccepted() => Assert.Equal(HttpStatusCode.OK, serve.Put("""{"order":[],"cards":{}}""").StatusCode);

        [Fact]
        public void NotesOfExactlyTheLimitAreAccepted() =>
            Assert.Equal(HttpStatusCode.OK, serve.Put(ServeScenario.Judgement([], (ServeScenario.First, 0, 3, new string('n', 2000)))).StatusCode);

        [Fact]
        public async Task TheReplyIsTheSavedDocument()
        {
            using HttpResponseMessage answer = serve.Put(ServeScenario.Judgement([ServeScenario.First]));
            Assert.Equal(ServeScenario.Now, Parse(await answer.Content.ReadAsStringAsync())["savedAt"]?.GetValue<DateTimeOffset>());
        }

        [Fact]
        public void ASavedAtFromThePageIsIgnored() => Assert.Equal(ServeScenario.Now, savedAt);

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioTheLastSaveWins : IDisposable
    {
        private readonly ServeScenario serve = new();
        private readonly JsonNode judgement;

        public ScenarioTheLastSaveWins()
        {
            serve.Accept(ServeScenario.Judgement([ServeScenario.First, ServeScenario.Second], (ServeScenario.First, 1, 5, "first"), (ServeScenario.Second, 0, 2, "second")));
            serve.Accept(ServeScenario.Judgement([ServeScenario.Second], (ServeScenario.Second, 3, 4, "changed")));
            judgement = Parse(serve.GetText("state"))["judgement"] ?? throw new InvalidOperationException("No judgement in state.");
        }

        [Fact]
        public void TheOrderIsTheSecondSaves() => Assert.Equal(1, judgement["order"]?.AsArray().Count);

        [Fact]
        public void TheFirstCardIsGone() => Assert.Null(judgement["cards"]?[ServeScenario.First]);

        [Fact]
        public void TheCountIsTheSecondSaves() => Assert.Equal(3, judgement["cards"]?[ServeScenario.Second]?["winces"]?.GetValue<int>());

        [Fact]
        public void NoTempFileIsLeftBehind() => Assert.False(File.Exists(serve.JudgementPath + ".tmp"));

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioQuickSavesTogether : IAsyncLifetime, IDisposable
    {
        private readonly ServeScenario serve = new();
        private HttpStatusCode[] statuses = [];

        public async Task InitializeAsync()
        {
            using HttpClient client = new() { BaseAddress = new Uri($"http://127.0.0.1:{serve.Port}/") };
            Task<HttpResponseMessage>[] saves =
            [
                .. Enumerable.Range(0, 20).Select(n =>
                    client.PutAsync("judgement", new StringContent(ServeScenario.Judgement([ServeScenario.First], (ServeScenario.First, n, 3, "")), System.Text.Encoding.UTF8, "application/json"))),
            ];
            HttpResponseMessage[] answers = await Task.WhenAll(saves);
            statuses = [.. answers.Select(a => a.StatusCode)];
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void EveryOneAnswers200() => Assert.Equal(Enumerable.Repeat(HttpStatusCode.OK, 20), statuses);

        [Fact]
        public void TheFileIsAWholeJudgement() => Assert.NotNull(Parse(File.ReadAllText(serve.JudgementPath))["cards"]?[ServeScenario.First]);

        public void Dispose() => serve.Dispose();
    }
}
