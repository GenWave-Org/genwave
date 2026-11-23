// STORY-566 — Every attempt is recorded and can be replayed (SPEC F220.7 + F220.29 · PLAN T781)
//
// BDD specification — xUnit. The chat snapshot source: pure, built from exactly the inputs the crosstalk writer
// already read, the same whatever is switched on (F220.7). Not wired into the writer yet.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Memory;

public static class FeatureChatSnapshot
{
    const long HostId = 7;
    const long NeighborId = 9;
    static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 30, 0, TimeSpan.FromHours(-6));

    sealed class PostureProbe(AudiencePosture posture) : IAudiencePostureProvider
    {
        public AudiencePosture Current => posture;
    }

    static PersonaCard Card(string name, string tagline, params string[] quirks) =>
        new(PersonaCard.CurrentSchemaVersion, name, "", "", [], new VoiceSpec("", "af_nova", 1.0, "en"), 0, [], [])
        {
            Tagline = tagline,
            QuirkRules = [.. quirks.Select(q => new QuirkRule(q))],
        };

    static CrosstalkExchangeRequest Request(long? hostId = HostId, long? neighborId = NeighborId) =>
        new(Card("Flip", "good grief", "Greets the weather"),
            Card("Tony", "oh boy", "Greets the weather", "Mutters at traffic"),
            "GenWave", null, null, Now, hostId, neighborId);

    static CrosstalkAiredScript Script() =>
        new([
            new CrosstalkAiredLine(CrosstalkSpeaker.Host, "Morning [laughs] Tony.", false),
            new CrosstalkAiredLine(CrosstalkSpeaker.Neighbor, "Morning Flip.", false),
            new CrosstalkAiredLine(CrosstalkSpeaker.Host, "Traffic is light.", true),
        ]);

    static UtteranceEntry Row(long id, long personaId, string text, TimeSpan ago, bool fallback = false) =>
        new(id, personaId, null, UtteranceKind.Crosstalk, "CrosstalkLine",
            ShiftKey.Unscheduled(personaId, new DateOnly(2026, 11, 22)), text, fallback, Now - ago, null);

    static ChatSnapshotInput Input(
        CrosstalkExchangeRequest? request = null,
        IReadOnlyList<UtteranceEntry>? hostRows = null,
        IReadOnlyList<UtteranceEntry>? neighborRows = null) =>
        new(request ?? Request(), 2, Script(), "America/Edmonton", 60, 220, hostRows ?? [], neighborRows ?? []);

    static TalkSnapshot Build(ChatSnapshotInput input) =>
        new ChatSnapshotSource(new PostureProbe(AudiencePosture.Mature), new TaglineLedger()).BuildAsync(input, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();

    // Feature: the writer's inputs land on the snapshot
    public sealed class ScenarioTheWritersInputsLandOnTheSnapshot
    {
        readonly TalkSnapshot snapshot = Build(Input());

        [Fact]
        public void TheSnapshotIsAChat() => Assert.Equal(TalkKinds.Chat, snapshot.Kind);

        [Fact]
        public void TheSegmentKindIsTheStoredCrosstalkKind() => Assert.Equal("CrosstalkLine", snapshot.SegmentKind);

        [Fact]
        public void TheAttemptIsCarried() => Assert.Equal(2, snapshot.Attempt);

        [Fact]
        public void TheLinesCarryTheRoleLabelsWithSoundTagsStripped() =>
            Assert.Equal(
                [new TalkLine("HOST", "Morning Tony."), new TalkLine("NEIGHBOR", "Morning Flip."), new TalkLine("HOST", "Traffic is light.")],
                snapshot.Lines.ToList());

        [Fact]
        public void TheStationLocalNowIsCarried() => Assert.Equal(Now, snapshot.LocalNow);

        [Fact]
        public void TheTimezoneIsCarried() => Assert.Equal("America/Edmonton", snapshot.Timezone);

        [Fact]
        public void TheStationCarriesTheNameAndPosture() =>
            Assert.Equal(new TalkStation("GenWave", "Mature"), snapshot.Station);

        [Fact]
        public void NoFactsAreOffered() => Assert.Null(snapshot.Facts);

        [Fact]
        public void NoOwnerMessageIsCarried() => Assert.Null(snapshot.OwnerMessageCore);

        [Fact]
        public void NoTrackIsCarried() => Assert.Null(snapshot.Track);

        [Fact]
        public void NoAdFieldsAreCarried() => Assert.Null(snapshot.Ad);

        [Fact]
        public void NoMemosAreLeft() => Assert.Equal(TalkMemos.Empty, snapshot.Memos);
    }

    // Feature: the chat fields carry what the length checks need
    public sealed class ScenarioTheChatFieldsCarryTheLengthLimits
    {
        readonly TalkSnapshot snapshot = Build(Input());

        [Fact]
        public void TheDurationTargetAndLineBudgetAndRateAndSidesAreCarried() =>
            Assert.Equal(new TalkChatFields(60, 220, 15.0, HostId, NeighborId, new TalkList<int>([22, 13, 17])), snapshot.Chat);

        [Fact]
        public void TheCarriedLengthKeepsTheSoundTagTheLineTextLoses() =>
            Assert.Equal(22, snapshot.Chat?.LineChars.First());
    }

    // Feature: speakers are host first, neighbor second, each with its card phrases
    public sealed class ScenarioTheSpeakersAreHostThenNeighbor
    {
        readonly TalkSnapshot snapshot = Build(Input());

        [Fact]
        public void HostIsFirstWithItsCardPhrases() =>
            Assert.Equal(
                new TalkSpeaker(HostId, "Flip", new TalkList<string>(["good grief", "Greets the weather"])),
                snapshot.Speakers[0]);

        [Fact]
        public void NeighborIsSecondWithItsCardPhrases() =>
            Assert.Equal(
                new TalkSpeaker(NeighborId, "Tony", new TalkList<string>(["oh boy", "Greets the weather", "Mutters at traffic"])),
                snapshot.Speakers[1]);
    }

    // Feature: recent lines hold each speaker's rows, fallbacks kept and flagged
    public sealed class ScenarioRecentLinesHoldBothSpeakersRows
    {
        readonly TalkSnapshot snapshot = Build(Input(
            hostRows: [Line(10, HostId, "Host template", 2, fallback: true), Line(8, HostId, "Host real", 30)],
            neighborRows: [Line(9, NeighborId, "Neighbor real", 10)]));

        static UtteranceEntry Line(long id, long personaId, string text, int minutesAgo, bool fallback = false) =>
            Row(id, personaId, text, TimeSpan.FromMinutes(minutesAgo), fallback);

        [Fact]
        public void TheRowsAreNewestFirst() =>
            Assert.Equal(["Host template", "Neighbor real", "Host real"], snapshot.RecentLines.Select(l => l.Text).ToList());

        [Fact]
        public void EachRowCarriesItsPersona() =>
            Assert.Equal([HostId, NeighborId, HostId], snapshot.RecentLines.Select(l => l.PersonaId).ToList());

        [Fact]
        public void AFallbackRowIsPresentAndFlagged() =>
            Assert.True(Assert.Single(snapshot.RecentLines, l => l.Text == "Host template").Fallback);

        [Fact]
        public void ARealRowIsNotFlaggedFallback() =>
            Assert.False(Assert.Single(snapshot.RecentLines, l => l.Text == "Host real").Fallback);

        [Fact]
        public void EachRowCarriesWhenItWasWritten() =>
            Assert.Equal(Now - TimeSpan.FromMinutes(2), snapshot.RecentLines[0].WrittenAt);
    }

    // Feature: a side without a persona id has no speaker and no history
    public sealed class ScenarioASideWithNoPersonaId
    {
        readonly TalkSnapshot snapshot = Build(Input(Request(hostId: null)));

        [Fact]
        public void OnlyTheIdentifiedSideIsASpeaker() =>
            Assert.Equal(NeighborId, Assert.Single(snapshot.Speakers).PersonaId);

        [Fact]
        public void TheChatFieldsSayWhichSideIsMissing() => Assert.Null(snapshot.Chat?.HostPersonaId);

        [Fact]
        public void TheLinesAreStillAllThere() => Assert.Equal(3, snapshot.Lines.Count);
    }

    // Feature: a speaker with no history
    public sealed class ScenarioEmptyHistory
    {
        [Fact]
        public void NoRecentLinesAreCarried() => Assert.Empty(Build(Input()).RecentLines);
    }

    // Feature: the same snapshot for the same input
    public sealed class ScenarioTheSameInputBuildsTheSameSnapshot
    {
        static ChatSnapshotInput Same() => Input(hostRows: [Row(1, HostId, "Earlier line", TimeSpan.FromMinutes(20))]);

        [Fact]
        public void TwoBuildsAreEqual() => Assert.Equal(Build(Same()), Build(Same()));
    }

    // Feature: the snapshot is storable
    public sealed class ScenarioTheSnapshotRoundTrips
    {
        readonly TalkSnapshot snapshot = Build(Input(hostRows: [Row(1, HostId, "Template", TimeSpan.FromMinutes(20), fallback: true)]));

        [Fact]
        public void ItEqualsTheOriginalAfterTheJsonCodec() =>
            Assert.Equal(snapshot, TalkSnapshotJson.Deserialize(TalkSnapshotJson.Serialize(snapshot)));
    }
}
