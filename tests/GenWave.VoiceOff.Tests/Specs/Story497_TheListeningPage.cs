// STORY-497 — The listening page (gh-#614 · SPEC F214.10 · PLAN T616)
//
// BDD specification — xUnit. The page itself (the done screen) is checked in the browser smoke;
// these facts hold the server side the page relies on, against the real serve host in-process.

using System.Text.Json;
using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureTheListeningPage
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — the LAN URL is printed
    public sealed class ScenarioTheLANURLIsPrinted : IDisposable
    {
        // Given `serve` on a port (0 here: the real bound port is asserted, so no spec needs 8765 to be free)
        // When  it starts
        private readonly ServeScenario serve = new();

        [Fact]
        public void StdoutShowsHttpLanIpPort() =>
            Assert.Equal($"Open http://{ServeScenario.LanIp}:{serve.Port}/ on a phone or laptop on this network.", serve.Stdout.Trim());

        public void Dispose() => serve.Dispose();
    }

    // AC2 — the page loads the session
    public sealed class ScenarioThePageLoadsTheSession : IDisposable
    {
        // Given a running serve
        // When  GET /session
        private readonly ServeScenario serve = new();
        private readonly string body;

        public ScenarioThePageLoadsTheSession() => body = serve.GetText("session?s=1&judge=Dean");

        [Fact]
        public void ItReturnsPairIdsAndOpaqueClipUrlsOnly()
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement first = doc.RootElement.GetProperty("pairs")[0];
            Assert.Equal(
                ["a", "b", "pairId"],
                first.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        }

        [Fact]
        public void EveryClipUrlIsAnOpaque32HexWav()
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            Assert.All(
                doc.RootElement.GetProperty("pairs").EnumerateArray().SelectMany(p => new[] { p.GetProperty("a").GetString(), p.GetProperty("b").GetString() }),
                url => Assert.Matches(@"\Aclips/[0-9a-f]{32}\.wav\z", url));
        }

        [Fact]
        public void TheCapIs40()
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            Assert.Equal(40, doc.RootElement.GetProperty("cap").GetInt32());
        }

        public void Dispose() => serve.Dispose();
    }

    // AC3 — a vote is appended
    public sealed class ScenarioAVoteIsAppended : IDisposable
    {
        // Given judge "Dean" picks a on pair p1 with why "robotic"
        // When  POST /vote
        private readonly ServeScenario serve = new();
        private readonly string pairId;
        private readonly HttpResponseMessage answer;

        public ScenarioAVoteIsAppended()
        {
            pairId = serve.PairIds()[0];
            answer = serve.Vote(pairId, "Dean", "a", "robotic");
        }

        private JsonElement Line()
        {
            using JsonDocument doc = JsonDocument.Parse(serve.VoteLines()[0]);
            return doc.RootElement.Clone();
        }

        [Fact]
        public void TheAnswerIs204() => Assert.Equal(System.Net.HttpStatusCode.NoContent, answer.StatusCode);

        [Fact]
        public void VotesJsonlGainsExactlyOneLine() => Assert.Single(serve.VoteLines());

        [Fact]
        public void TheLineNamesThePair() => Assert.Equal(pairId, Line().GetProperty("pairId").GetString());

        [Fact]
        public void TheLineNamesTheJudge() => Assert.Equal("Dean", Line().GetProperty("judge").GetString());

        [Fact]
        public void TheLineHoldsThePick() => Assert.Equal("a", Line().GetProperty("pick").GetString());

        [Fact]
        public void TheLineHoldsTheWhy() => Assert.Equal("robotic", Line().GetProperty("why").GetString());

        [Fact]
        public void TheLineHoldsTheTimestampOfTheInjectedClock() =>
            Assert.Equal(ServeScenario.Now, Line().GetProperty("at").GetDateTimeOffset());

        [Fact]
        public void TheLineHasNothingElse() =>
            Assert.Equal(["at", "judge", "pairId", "pick", "why"], Line().EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));

        public void Dispose() => serve.Dispose();
    }

    // AC4 — progress is shown
    public sealed class ScenarioProgressIsShown : IDisposable
    {
        // Given 11 votes from "Dean" this session
        // When  GET /progress?judge=Dean
        private readonly ServeScenario serve = new();
        private readonly IReadOnlyDictionary<string, string> progress;

        public ScenarioProgressIsShown()
        {
            foreach (string pairId in serve.PairIds().Take(11))
            {
                serve.Vote(pairId, "Dean", "b");
            }

            progress = serve.Progress("Dean");
        }

        [Fact]
        public void ItAnswers11Done() => Assert.Equal("11", progress["done"]);

        [Fact]
        public void ItAnswersACapOf40() => Assert.Equal("40", progress["cap"]);

        public void Dispose() => serve.Dispose();
    }

    // AC5 — the cap holds
    public sealed class ScenarioTheCapHolds : IDisposable
    {
        // Given 40 votes from "Dean" this session
        // When  the page asks for the next pair (it reads /progress, then /session)
        private readonly ServeScenario serve = new();
        private readonly IReadOnlyDictionary<string, string> progress;
        private readonly IReadOnlyList<string> order;

        public ScenarioTheCapHolds()
        {
            foreach (string pairId in serve.PairIds())
            {
                serve.Vote(pairId, "Dean", "tie");
            }

            progress = serve.Progress("Dean");
            order = serve.SessionOrder("Dean");
        }

        [Fact]
        public void ProgressAnswersDone40OfCap40() => Assert.Equal(("40", "40"), (progress["done"], progress["cap"]));

        [Fact]
        public void SessionStillReturnsTheList() => Assert.Equal(40, order.Count);

        public void Dispose() => serve.Dispose();
    }

    // AC6 — order is shuffled per judge
    public sealed class ScenarioOrderIsShuffledPerJudge : IDisposable
    {
        // Given two judges on the same session
        // When  each loads /session
        private readonly ServeScenario serve = new();
        private readonly IReadOnlyList<string> dean;
        private readonly IReadOnlyList<string> alex;

        public ScenarioOrderIsShuffledPerJudge()
        {
            dean = serve.SessionOrder("Dean");
            alex = serve.SessionOrder("Alex");
        }

        [Fact]
        public void TheirPairOrdersDiffer() => Assert.NotEqual(dean, alex);

        [Fact]
        public void TheyStillHoldTheSamePairs() => Assert.Equal(dean.Order(StringComparer.Ordinal), alex.Order(StringComparer.Ordinal));

        [Fact]
        public void AJudgeKeepsTheirOrderOnReload() => Assert.Equal(dean, serve.SessionOrder("Dean"));

        public void Dispose() => serve.Dispose();
    }

    // AC7 — a re-vote replaces
    public sealed class ScenarioAReVoteReplaces : IDisposable
    {
        // Given "Dean" voted a then b on p1
        // When  effective votes are read (through the reader and last-wins rule score uses)
        private readonly ServeScenario serve = new();
        private readonly Dictionary<string, Dictionary<string, VotePick>> effective;
        private readonly string pairId;

        public ScenarioAReVoteReplaces()
        {
            pairId = serve.PairIds()[0];
            serve.Vote(pairId, "Dean", "a");
            serve.Vote(pairId, "Dean", "b");
            PairManifest manifest = serve.Run.Manifest();
            effective = EffectiveVotes.Last(VotesReader.ReadAsync(serve.VotesPath, "votes.jsonl", manifest, CancellationToken.None).GetAwaiter().GetResult());
        }

        [Fact]
        public void DeanSP1VoteIsB() => Assert.Equal(VotePick.B, effective[pairId]["Dean"]);

        [Fact]
        public void BothLinesStayInTheFile() => Assert.Equal(2, serve.VoteLines().Count);

        [Fact]
        public void ProgressCountsThePairOnce() => Assert.Equal("1", serve.Progress("Dean")["done"]);

        public void Dispose() => serve.Dispose();
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC8 — a bad judge name is refused
    public sealed class ScenarioABadJudgeNameIsRefused : IDisposable
    {
        // Given judge "<script>"
        // When  POST /vote
        private readonly ServeScenario serve = new();
        private readonly HttpResponseMessage answer;
        private readonly byte[] before;

        public ScenarioABadJudgeNameIsRefused()
        {
            serve.AcceptVote(serve.PairIds()[0], "Dean", "a");
            before = serve.VotesBytes();
            answer = serve.Vote(serve.PairIds()[0], "<script>", "a");
        }

        [Fact]
        public void TheAnswerIs400() => Assert.Equal(System.Net.HttpStatusCode.BadRequest, answer.StatusCode);

        [Fact]
        public void VotesJsonlIsUnchanged() => Assert.Equal(before, serve.VotesBytes());

        public void Dispose() => serve.Dispose();
    }

    // AC9 — an unknown why-tag is refused
    public sealed class ScenarioAnUnknownWhyTagIsRefused : IDisposable
    {
        // Given why "loud"
        // When  POST /vote
        private readonly ServeScenario serve = new();
        private readonly HttpResponseMessage answer;
        private readonly byte[] before;

        public ScenarioAnUnknownWhyTagIsRefused()
        {
            serve.AcceptVote(serve.PairIds()[0], "Alex", "a");
            before = serve.VotesBytes();
            answer = serve.Vote(serve.PairIds()[0], "Dean", "a", "loud");
        }

        [Fact]
        public void TheAnswerIs400() => Assert.Equal(System.Net.HttpStatusCode.BadRequest, answer.StatusCode);

        [Fact]
        public void VotesJsonlIsUnchanged() => Assert.Equal(before, serve.VotesBytes());

        public void Dispose() => serve.Dispose();
    }

    // AC10 — an oversize body is refused
    public sealed class ScenarioAnOversizeBodyIsRefused : IDisposable
    {
        // Given a 5 KB body
        // When  POST /vote
        private readonly ServeScenario serve = new();
        private readonly HttpResponseMessage answer;
        private readonly byte[] before;

        public ScenarioAnOversizeBodyIsRefused()
        {
            serve.AcceptVote(serve.PairIds()[0], "Alex", "b");
            before = serve.VotesBytes();
            answer = serve.Post(ServeScenario.VoteJson(serve.PairIds()[0], "Dean", "a") + new string(' ', 5 * 1024));
        }

        [Fact]
        public void TheAnswerIs413() => Assert.Equal(System.Net.HttpStatusCode.RequestEntityTooLarge, answer.StatusCode);

        [Fact]
        public void VotesJsonlIsUnchanged() => Assert.Equal(before, serve.VotesBytes());

        public void Dispose() => serve.Dispose();
    }

    // AC11 — the manifest is not served
    public sealed class ScenarioTheManifestIsNotServed : IDisposable
    {
        // Given a running serve
        // When  GET /../manifest.json and GET /manifest.json
        private readonly ServeScenario serve = new();

        [Fact]
        public void TheDotDotPathAnswers404() => Assert.Equal(404, serve.RawStatus("/../manifest.json"));

        [Fact]
        public void TheBarePathAnswers404() => Assert.Equal(404, serve.RawStatus("/manifest.json"));

        public void Dispose() => serve.Dispose();
    }
}
