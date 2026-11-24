// STORY-566 — Every attempt is recorded and can be replayed (SPEC F220.7 + F220.29 · PLAN T782)
//
// BDD specification — xUnit. The ad snapshot source: pure, built from exactly the inputs the ad writer already
// holds, the same whatever is switched on (F220.7). Not wired into the writer yet.

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Fakes;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;

public static class FeatureAdSnapshot
{
    const string SponsorPhone = "406-222-0100";
    const string Reply =
        "ANNOUNCER: Call Cravin's Diner at 406-222-0199 [laughs] today.\n" +
        "VOICE1: (warmly) Great food, great folks.";
    static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 30, 0, TimeSpan.FromHours(-6));

    static AdSnapshotInput Input(
        string reply = Reply, string? phone = SponsorPhone, bool packOwned = false,
        AudiencePosture posture = AudiencePosture.Everyone) =>
        new(reply, new AdScriptValidationRequest(posture, 220, 30, 0.4, "Cravin's Diner", phone, packOwned),
            "GenWave", Now, "America/Edmonton", 2);

    static TalkSnapshot Build(AdSnapshotInput input, FakePatterDurationEstimator? estimator = null) =>
        new AdSnapshotSource(estimator ?? new FakePatterDurationEstimator()).BuildAsync(input, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();

    // Feature: the writer's inputs land on the snapshot
    public sealed class ScenarioTheWritersInputsLandOnTheSnapshot
    {
        readonly TalkSnapshot snapshot = Build(Input());

        [Fact]
        public void TheSnapshotIsAnAd() => Assert.Equal(TalkKinds.Ad, snapshot.Kind);

        [Fact]
        public void TheSegmentKindIsAd() => Assert.Equal("Ad", snapshot.SegmentKind);

        [Fact]
        public void TheAttemptIsCarried() => Assert.Equal(2, snapshot.Attempt);

        [Fact]
        public void TheLinesCarryTheVoiceTags() =>
            Assert.Equal(["ANNOUNCER", "VOICE1"], snapshot.Lines.Select(l => l.Speaker).ToList());

        [Fact]
        // Line-aware hygiene alone strips the bracket; the source adds no strip of its own.
        public void TheSoundTagIsStrippedByHygiene() => Assert.DoesNotContain("[laughs]", snapshot.Lines[0].Text, StringComparison.Ordinal);

        [Fact]
        public void TheStageDirectionIsCleanedAway() =>
            Assert.Equal("Great food, great folks.", snapshot.Lines[1].Text);

        [Fact]
        public void TheCleanedTextKeepsThePhoneAsWritten() =>
            Assert.Equal("Call Cravin's Diner at 406-222-0199 today.", snapshot.Lines[0].Text);

        [Fact]
        public void TheStationCarriesTheNameAndPosture() =>
            Assert.Equal(new TalkStation("GenWave", "Everyone"), snapshot.Station);

        [Fact]
        public void TheStationLocalNowIsCarried() => Assert.Equal(Now, snapshot.LocalNow);

        [Fact]
        public void TheTimezoneIsCarried() => Assert.Equal("America/Edmonton", snapshot.Timezone);

        [Fact]
        public void ThereAreNoSpeakers() => Assert.Empty(snapshot.Speakers);

        [Fact]
        public void ThereAreNoRecentLines() => Assert.Empty(snapshot.RecentLines);

        [Fact]
        public void NoFactsAreOffered() => Assert.Null(snapshot.Facts);

        [Fact]
        public void NoOwnerMessageIsCarried() => Assert.Null(snapshot.OwnerMessageCore);

        [Fact]
        public void NoTrackIsCarried() => Assert.Null(snapshot.Track);

        [Fact]
        public void NoChatFieldsAreCarried() => Assert.Null(snapshot.Chat);

        [Fact]
        public void NoMemosAreLeft() => Assert.Equal(TalkMemos.Empty, snapshot.Memos);
    }

    // Feature: the ad fields carry the sponsor and the limits
    public sealed class ScenarioTheAdFieldsCarryTheSponsorAndLimits
    {
        readonly TalkAdFields? ad = Build(Input()).Ad;

        [Fact]
        public void TheSponsorNameIsCarried() => Assert.Equal("Cravin's Diner", ad?.SponsorName);

        [Fact]
        public void TheSponsorPhoneIsCarried() => Assert.Equal(SponsorPhone, ad?.SponsorPhone);

        [Fact]
        public void TheSpotSecondsAreCarried() => Assert.Equal(30, ad?.SpotSeconds);

        [Fact]
        public void AnOwnerSponsorIsNotPackOwned() => Assert.False(ad?.PackOwned);

        [Fact]
        public void TheToleranceRatioIsCarried() => Assert.Equal(0.4, ad?.ToleranceRatio);

        [Fact]
        public void TheLineBudgetIsCarried() => Assert.Equal(220, ad?.MaxLineChars);

        [Fact]
        public void TheSpokenRateIsTheValidatorsOwn() => Assert.Equal(15.0, ad?.CharsPerSecond);
    }

    // Feature: a measured duration estimate is carried per line, an untested one is not
    public sealed class ScenarioTheObservedEstimateIsCarriedOnlyWhenMeasured
    {
        static TalkAdFields? Ad(PatterEstimateConfidence confidence) =>
            Build(Input(), new FakePatterDurationEstimator { Confidence = confidence, DurationPerCall = TimeSpan.FromSeconds(7) }).Ad;

        [Fact]
        public void AHeuristicAnswerIsCarriedAsZero() =>
            Assert.Equal([0.0, 0.0], Ad(PatterEstimateConfidence.Heuristic)?.ObservedLineSeconds?.ToList());

        [Fact]
        public void AHistoricalAnswerIsCarriedPerLine() =>
            Assert.Equal([7.0, 7.0], Ad(PatterEstimateConfidence.Historical)?.ObservedLineSeconds?.ToList());

        [Fact]
        public void AnExactAnswerIsCarriedPerLine() =>
            Assert.Equal([7.0, 7.0], Ad(PatterEstimateConfidence.Exact)?.ObservedLineSeconds?.ToList());
    }

    // Feature: a pack sponsor
    public sealed class ScenarioAPackOwnedSponsor
    {
        [Fact]
        public void ThePackFlagIsCarried() => Assert.True(Build(Input(packOwned: true)).Ad?.PackOwned);
    }

    // Feature: the sad path, no phone on file and a phone the old rewrite would have changed
    public sealed class ScenarioNoSponsorPhoneAndADifferentNumberInTheScript
    {
        readonly TalkSnapshot snapshot = Build(Input(phone: null));

        [Fact]
        public void NoSponsorPhoneIsCarried() => Assert.Null(snapshot.Ad?.SponsorPhone);

        [Fact]
        public void TheScriptsPhoneIsStillAsTheModelWroteIt() =>
            Assert.Contains("406-222-0199", snapshot.Lines[0].Text, StringComparison.Ordinal);
    }

    // Feature: a reply the format parser refuses
    public sealed class ScenarioAReplyTheFormatParserRefuses
    {
        readonly TalkSnapshot snapshot = Build(Input(reply: ""));

        [Fact]
        public void ThereAreNoLines() => Assert.Empty(snapshot.Lines);

        [Fact]
        public void ThereAreNoObservedSeconds() => Assert.Equal(0, snapshot.Ad?.ObservedLineSeconds?.Count);
    }

    // Feature: the same snapshot for the same input
    public sealed class ScenarioTheSameInputBuildsTheSameSnapshot
    {
        [Fact]
        public void TwoBuildsAreEqual() => Assert.Equal(Build(Input()), Build(Input()));
    }

    // Feature: the snapshot is storable
    public sealed class ScenarioTheSnapshotRoundTrips
    {
        readonly TalkSnapshot snapshot = Build(Input());

        [Fact]
        public void ItEqualsTheOriginalAfterTheJsonCodec() =>
            Assert.Equal(snapshot, TalkSnapshotJson.Deserialize(TalkSnapshotJson.Serialize(snapshot)));
    }
}
