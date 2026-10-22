// STORY-497 — The listening page (gh-#614 · SPEC F214.10 · PLAN T616)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-21); the named task
// activates its facts, watches them fail, then makes them pass.

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureTheListeningPage
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — the LAN URL is printed
    public sealed class ScenarioTheLANURLIsPrinted
    {
        // Given `serve` on port 8765
        // When  it starts

        [Fact(Skip = "pending: T616 — see docs/PLAN.md")]
        public void StdoutShowsHttpLanIp8765() => Assert.Fail("pending: T616 — stdout shows http://<lan-ip>:8765/");
    }

    // AC2 — the page loads the session
    public sealed class ScenarioThePageLoadsTheSession
    {
        // Given a running serve
        // When  GET /session

        [Fact(Skip = "pending: T616 — see docs/PLAN.md")]
        public void ItReturnsPairIdsAndOpaqueClipUrlsOnly() => Assert.Fail("pending: T616 — it returns pair ids and opaque clip urls only");
    }

    // AC3 — a vote is appended
    public sealed class ScenarioAVoteIsAppended
    {
        // Given judge "Dean" picks a on pair p1 with why "robotic"
        // When  POST /vote

        [Fact(Skip = "pending: T616 — see docs/PLAN.md")]
        public void VotesJsonlGainsOneLinePairIdP1JudgeDeanPickAWhyRoboticAt() => Assert.Fail("pending: T616 — votes.jsonl gains one line {pairId:p1, judge:'Dean', pick:'a', why:'robotic', at}");
    }

    // AC4 — progress is shown
    public sealed class ScenarioProgressIsShown
    {
        // Given 11 votes from "Dean" this session
        // When  GET /progress?judge=Dean

        [Fact(Skip = "pending: T616 — see docs/PLAN.md")]
        public void ItAnswers11Of40() => Assert.Fail("pending: T616 — it answers 11 of 40");
    }

    // AC5 — the cap holds
    public sealed class ScenarioTheCapHolds
    {
        // Given 40 votes from "Dean" this session
        // When  the page asks for the next pair

        [Fact(Skip = "pending: T616 — see docs/PLAN.md")]
        public void ItShowsTheDoneScreen() => Assert.Fail("pending: T616 — it shows the done screen");
    }

    // AC6 — order is shuffled per judge
    public sealed class ScenarioOrderIsShuffledPerJudge
    {
        // Given two judges on the same session
        // When  each loads /session

        [Fact(Skip = "pending: T616 — see docs/PLAN.md")]
        public void TheirPairOrdersDiffer() => Assert.Fail("pending: T616 — their pair orders differ");
    }

    // AC7 — a re-vote replaces
    public sealed class ScenarioAReVoteReplaces
    {
        // Given "Dean" voted a then b on p1
        // When  effective votes are read

        [Fact(Skip = "pending: T616 — see docs/PLAN.md")]
        public void DeanSP1VoteIsB() => Assert.Fail("pending: T616 — Dean's p1 vote is b");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC8 — a bad judge name is refused
    public sealed class ScenarioABadJudgeNameIsRefused
    {
        // Given judge "<script>"
        // When  POST /vote

        [Fact(Skip = "pending: T616 — see docs/PLAN.md")]
        public void TheAnswerIs400AndVotesJsonlIsUnchanged() => Assert.Fail("pending: T616 — the answer is 400 and votes.jsonl is unchanged");
    }

    // AC9 — an unknown why-tag is refused
    public sealed class ScenarioAnUnknownWhyTagIsRefused
    {
        // Given why "loud"
        // When  POST /vote

        [Fact(Skip = "pending: T616 — see docs/PLAN.md")]
        public void TheAnswerIs400() => Assert.Fail("pending: T616 — the answer is 400");
    }

    // AC10 — an oversize body is refused
    public sealed class ScenarioAnOversizeBodyIsRefused
    {
        // Given a 5 KB body
        // When  POST /vote

        [Fact(Skip = "pending: T616 — see docs/PLAN.md")]
        public void TheAnswerIs413() => Assert.Fail("pending: T616 — the answer is 413");
    }

    // AC11 — the manifest is not served
    public sealed class ScenarioTheManifestIsNotServed
    {
        // Given a running serve
        // When  GET /../manifest.json and GET /manifest.json

        [Fact(Skip = "pending: T616 — see docs/PLAN.md")]
        public void BothAnswer404() => Assert.Fail("pending: T616 — both answer 404");
    }
}
