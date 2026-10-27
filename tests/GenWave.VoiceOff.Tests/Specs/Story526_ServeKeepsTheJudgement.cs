// STORY-526 — serve keeps the judgement (gh-#614 · SPEC F217 · PLAN T664)
//
// BDD specification — xUnit. Active since T664.

using System.Net;
using System.Text.Json.Nodes;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureServeKeepsTheJudgement
{
    private static JsonNode Parse(string json) =>
        JsonNode.Parse(json) ?? throw new InvalidOperationException("The reply was JSON null.");

    private static string[] Ids(JsonNode? array) =>
        [.. (array?.AsArray() ?? []).Select(n => n?.GetValue<string>() ?? string.Empty)];

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — state lists every card
    public sealed class ScenarioStateListsEveryCard : IDisposable
    {
        // Given a run with two rendered cards and one failed
        // When  GET /state
        private readonly ServeScenario serve = new();
        private readonly JsonNode state;

        public ScenarioStateListsEveryCard() => state = Parse(serve.GetText("state"));

        private JsonNode Card(string id) =>
            state["cards"]?.AsArray().First(c => c?["id"]?.GetValue<string>() == id) ?? throw new InvalidOperationException($"No card {id}.");

        [Fact]
        public void ThreeCardsAreReturned() => Assert.Equal(3, state["cards"]?.AsArray().Count);

        [Fact]
        public void EachRenderedCardCarriesItsFacts() =>
            Assert.Equal(["cp-1", "cp-1"], new[] { ServeScenario.First, ServeScenario.Second }.Select(id => Card(id)["details"]?["facts"]?["checkpoint"]?.GetValue<string>()));

        [Fact]
        public void TheFailedCardCarriesItsReason() => Assert.Equal(ServeScenario.FailureReason, Card(ServeScenario.Third)["reason"]?.GetValue<string>());

        public void Dispose() => serve.Dispose();
    }

    // AC2 — clips are served
    public sealed class ScenarioClipsAreServed : IDisposable
    {
        // Given the run
        // When  GET /clips/<card>.wav
        private readonly ServeScenario serve = new();
        private readonly HttpResponseMessage answer;

        public ScenarioClipsAreServed() => answer = serve.Get($"clips/{ServeScenario.First}.wav");

        [Fact]
        public async Task TheMatchedClipBytesComeBack() => Assert.Equal(serve.FirstClip, await answer.Content.ReadAsByteArrayAsync());

        [Fact]
        public void TheContentTypeIsAudioWav() => Assert.Equal("audio/wav", answer.Content.Headers.ContentType?.MediaType);

        public void Dispose() => serve.Dispose();
    }

    // AC3 — a judgement is saved
    public sealed class ScenarioAJudgementIsSaved : IDisposable
    {
        // Given a judgement with order, winces, stars and notes
        // When  PUT /judgement
        private readonly ServeScenario serve = new();
        private readonly JsonNode saved;

        public ScenarioAJudgementIsSaved()
        {
            serve.Accept(ServeScenario.Judgement([ServeScenario.Second, ServeScenario.First], (ServeScenario.First, 2, 4, "warm"), (ServeScenario.Second, 0, 5, "best")));
            saved = Parse(File.ReadAllText(serve.JudgementPath));
        }

        [Fact]
        public void JudgementJsonHoldsTheOrder() => Assert.Equal([ServeScenario.Second, ServeScenario.First], Ids(saved["order"]));

        [Fact]
        public void JudgementJsonHoldsTheWincesStarsAndNotes() =>
            Assert.Equal("2/4/warm", $"{saved["cards"]?[ServeScenario.First]?["winces"]}/{saved["cards"]?[ServeScenario.First]?["stars"]}/{saved["cards"]?[ServeScenario.First]?["notes"]}");

        [Fact]
        public void SavedAtIsSet() => Assert.Equal(ServeScenario.Now, saved["savedAt"]?.GetValue<DateTimeOffset>());

        public void Dispose() => serve.Dispose();
    }

    // AC4 — state returns the judgement
    public sealed class ScenarioStateReturnsTheJudgement : IDisposable
    {
        // Given the saved judgement
        // When  GET /state
        private readonly ServeScenario serve = new();
        private readonly JsonNode judgement;

        public ScenarioStateReturnsTheJudgement()
        {
            serve.Accept(ServeScenario.Judgement([ServeScenario.Third, ServeScenario.First, ServeScenario.Second], (ServeScenario.Third, 1, 3, "a bit flat")));
            judgement = Parse(serve.GetText("state"))["judgement"] ?? throw new InvalidOperationException("No judgement in state.");
        }

        [Fact]
        public void TheOrderIsReturnedAsSaved() => Assert.Equal([ServeScenario.Third, ServeScenario.First, ServeScenario.Second], Ids(judgement["order"]));

        [Fact]
        public void TheNotesAreReturnedAsSaved() => Assert.Equal("a bit flat", judgement["cards"]?[ServeScenario.Third]?["notes"]?.GetValue<string>());

        public void Dispose() => serve.Dispose();
    }

    // AC5 — missing cards sort last
    public sealed class ScenarioMissingCardsSortLast : IDisposable
    {
        // Given an order naming one of three cards
        // When  GET /state
        private readonly ServeScenario serve = new();
        private readonly string[] shown;

        public ScenarioMissingCardsSortLast()
        {
            serve.Accept(ServeScenario.Judgement([ServeScenario.Third]));
            shown = [.. (Parse(serve.GetText("state"))["cards"]?.AsArray() ?? []).Select(c => c?["id"]?.GetValue<string>() ?? string.Empty)];
        }

        [Fact]
        public void TheUnnamedCardsFollowInIdOrder() => Assert.Equal([ServeScenario.Third, ServeScenario.First, ServeScenario.Second], shown);

        public void Dispose() => serve.Dispose();
    }

    // AC6 — the LAN URL is printed
    public sealed class ScenarioTheLANURLIsPrinted : IDisposable
    {
        // Given serve starts
        // When  stdout is read
        private readonly ServeScenario serve = new(bind: System.Net.IPAddress.Any);

        [Fact]
        public void ItPrintsAURLOnALANAddress() =>
            Assert.Contains($"Open http://{ServeScenario.LanIp}:{serve.Port}/", serve.Stdout, StringComparison.Ordinal);

        public void Dispose() => serve.Dispose();
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC7 — an unknown card id is refused
    public sealed class ScenarioAnUnknownCardIdIsRefused : IDisposable
    {
        // Given an order naming a card not in the run
        // When  PUT /judgement
        private readonly ServeScenario serve = new();
        private readonly byte[] before;
        private readonly HttpResponseMessage answer;

        public ScenarioAnUnknownCardIdIsRefused()
        {
            serve.Accept(ServeScenario.Judgement([ServeScenario.First], (ServeScenario.First, 0, 3, "kept")));
            before = serve.JudgementBytes();
            answer = serve.Put(ServeScenario.Judgement([ServeScenario.First, "zzz-nothing"]));
        }

        [Fact]
        public void TheReplyIs400() => Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);

        [Fact]
        public void JudgementJsonIsUnchanged() => Assert.Equal(before, serve.JudgementBytes());

        public void Dispose() => serve.Dispose();
    }

    // AC8 — an oversized body is refused
    public sealed class ScenarioAnOversizedBodyIsRefused : IDisposable
    {
        // Given a body over 64 KB
        // When  PUT /judgement
        private readonly ServeScenario serve = new();
        private readonly HttpResponseMessage answer;

        public ScenarioAnOversizedBodyIsRefused() =>
            answer = serve.Put(ServeScenario.Judgement([ServeScenario.First]) + new string(' ', 64 * 1024));

        [Fact]
        public void TheReplyIs413() => Assert.Equal(HttpStatusCode.RequestEntityTooLarge, answer.StatusCode);

        public void Dispose() => serve.Dispose();
    }

    // AC9 — out-of-range scores are refused
    public sealed class ScenarioOutOfRangeScoresAreRefused : IDisposable
    {
        // Given stars 6 and winces -1
        // When  PUT /judgement
        private readonly ServeScenario serve = new();
        private readonly HttpStatusCode tooManyStars;
        private readonly HttpStatusCode negativeWinces;

        public ScenarioOutOfRangeScoresAreRefused()
        {
            tooManyStars = serve.Put(ServeScenario.Judgement([ServeScenario.First], (ServeScenario.First, 0, 6, ""))).StatusCode;
            negativeWinces = serve.Put(ServeScenario.Judgement([ServeScenario.First], (ServeScenario.First, -1, 3, ""))).StatusCode;
        }

        [Fact]
        public void StarsOf6AreRefusedWith400() => Assert.Equal(HttpStatusCode.BadRequest, tooManyStars);

        [Fact]
        public void NegativeWincesAreRefusedWith400() => Assert.Equal(HttpStatusCode.BadRequest, negativeWinces);

        public void Dispose() => serve.Dispose();
    }

    // AC10 — traversal is refused
    public sealed class ScenarioTraversalIsRefused : IDisposable
    {
        // Given GET /clips/../run.json
        // When  the request is made
        private readonly ServeScenario serve = new();

        [Fact]
        public void TheReplyIs404() => Assert.Equal(404, serve.RawStatus("/clips/../run.json"));

        public void Dispose() => serve.Dispose();
    }

    // AC11 — malformed JSON is refused
    public sealed class ScenarioMalformedJSONIsRefused : IDisposable
    {
        // Given a body that is not JSON
        // When  PUT /judgement
        private readonly ServeScenario serve = new();

        [Fact]
        public void TheReplyIs400() => Assert.Equal(HttpStatusCode.BadRequest, serve.Put("order: first, then second").StatusCode);

        public void Dispose() => serve.Dispose();
    }
}
