// STORY-566 — Every attempt is recorded and can be replayed (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

using System.Text.Json;
using GenWave.Core.TalkRules;

namespace GenWave.Core.Tests.Specs;

public static class FeatureSnapshotRoundTrip
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 30, 0, TimeSpan.FromHours(-6));

    private static TalkSnapshot Snapshot(TalkKinds kind, TalkAdFields? ad, TalkMemos memos) => new(
        kind,
        "banter",
        2,
        new TalkList<TalkLine>([new TalkLine("flip", "That was a good one."), new TalkLine("tony", "Wasn't it.")]),
        new TalkList<TalkSpeaker>([new TalkSpeaker(7, "Flip", new TalkList<string>(["stay golden", "oh boy"]))]),
        new TalkList<string>(["It is snowing in Calgary."]),
        "Say hi to Dean.",
        Now,
        "America/Edmonton",
        new TalkTrack("Song A", "Artist A", "Album A", 1984, new TalkTrack("Song B", "Artist B", null, null, null)),
        new TalkStation("GenWave", "private, at home"),
        new TalkList<TalkRecentLine>([new TalkRecentLine(7, "Earlier line.", Now.AddMinutes(-20))]),
        ad,
        memos);

    private static TalkSnapshot RoundTrip(TalkSnapshot original) =>
        TalkSnapshotJson.Deserialize(TalkSnapshotJson.Serialize(original));

    // AC5 — a snapshot stored as JSON and read back
    public sealed class ScenarioASnapshotIsStoredAndReadBack
    {
        private readonly TalkSnapshot djLine = Snapshot(TalkKinds.DjLine, null, TalkMemos.Empty);
        private readonly TalkSnapshot chat = Snapshot(TalkKinds.Chat, null, TalkMemos.Empty);
        private readonly TalkSnapshot ad = Snapshot(TalkKinds.Ad, new TalkAdFields("Acme", "555-0100", 30, true, 0.4, 220, 15.0, TalkList<double>.Empty), TalkMemos.Empty);
        private readonly TalkSnapshot withMemos;
        private readonly TalkSnapshot withoutFacts;

        public ScenarioASnapshotIsStoredAndReadBack()
        {
            using var memo = JsonDocument.Parse("""{"offered":["stay golden"]}""");
            withMemos = Snapshot(TalkKinds.DjLine, null, TalkMemos.Empty.With("catchphrase-limits", memo.RootElement));
            withoutFacts = Snapshot(TalkKinds.DjLine, null, TalkMemos.Empty) with { Facts = null };
        }

        [Fact]
        public void ADjLineSnapshotEqualsTheOriginal() => Assert.Equal(djLine, RoundTrip(djLine));

        [Fact]
        public void AChatSnapshotEqualsTheOriginal() => Assert.Equal(chat, RoundTrip(chat));

        [Fact]
        public void AnAdSnapshotEqualsTheOriginal() => Assert.Equal(ad, RoundTrip(ad));

        [Fact]
        public void AChatSnapshotWithItsChatFieldsEqualsTheOriginal()
        {
            var withChatFields = chat with { Chat = new TalkChatFields(60, 220, 15.0, 7, null, new TalkList<int>([22, 13])) };
            Assert.Equal(withChatFields, RoundTrip(withChatFields));
        }

        [Fact]
        public void AnAdSnapshotWithItsLimitFieldsEqualsTheOriginal()
        {
            var withLengths = ad with
            {
                Ad = new TalkAdFields("Acme", null, 30, false, 0.4, 220, 15.0, new TalkList<double>([0, 7.5])),
            };
            Assert.Equal(withLengths, RoundTrip(withLengths));
        }

        [Fact]
        public void MemosSurviveTheRoundTrip() => Assert.Equal(withMemos, RoundTrip(withMemos));

        [Fact]
        public void TheLocalOffsetSurvivesTheRoundTrip() => Assert.Equal(TimeSpan.FromHours(-6), RoundTrip(djLine).LocalNow.Offset);

        [Fact]
        public void NullFactsComeBackNull() => Assert.Null(RoundTrip(withoutFacts).Facts);

        [Fact]
        public void EmptyFactsStayEmptyNotNull() =>
            Assert.Empty(RoundTrip(djLine with { Facts = TalkList<string>.Empty }).Facts ?? throw new InvalidOperationException("Facts became null."));
    }

    // The stored spellings other code (SQL, the replay tool) depends on
    public sealed class ScenarioTheStoredSpelling
    {
        private readonly string djLine = TalkSnapshotJson.Serialize(Snapshot(TalkKinds.DjLine, null, TalkMemos.Empty));
        private readonly string chat = TalkSnapshotJson.Serialize(Snapshot(TalkKinds.Chat, null, TalkMemos.Empty));
        private readonly string ad = TalkSnapshotJson.Serialize(Snapshot(TalkKinds.Ad, new TalkAdFields("Acme", "555-0100", 30, true, 0.4, 220, 15.0, TalkList<double>.Empty), TalkMemos.Empty));
        private readonly string withMemo;
        private readonly string withUnsortedMemo;

        public ScenarioTheStoredSpelling()
        {
            using var memo = JsonDocument.Parse("""{ "a": 1 }""");
            withMemo = TalkSnapshotJson.Serialize(Snapshot(TalkKinds.DjLine, null, TalkMemos.Empty.With("r", memo.RootElement)));
            using var unsorted = JsonDocument.Parse("""{ "b": [ {"z": 1, "y": 2} ], "a": { "d": 1, "c": 2 } }""");
            withUnsortedMemo = TalkSnapshotJson.Serialize(Snapshot(TalkKinds.DjLine, null, TalkMemos.Empty.With("r", unsorted.RootElement)));
        }

        [Fact]
        public void ADjLineKindIsSpelledDjLine() => Assert.Contains("\"kind\":\"dj-line\"", djLine);

        [Fact]
        public void AnAdKindIsSpelledAd() => Assert.Contains("\"kind\":\"ad\"", ad);

        [Fact]
        public void AChatKindIsSpelledChat() => Assert.Contains("\"kind\":\"chat\"", chat);

        [Fact]
        public void KeysAreCamelCase() => Assert.Contains("\"localNow\"", djLine);

        [Fact]
        public void MemosAreStoredAsRealJsonKeyedByRuleId() => Assert.Contains("\"memos\":{\"r\":{\"a\":1}}", withMemo);

        [Fact]
        public void MemoKeysAreSortedAtEveryDepth() =>
            Assert.Contains("\"memos\":{\"r\":{\"a\":{\"c\":2,\"d\":1},\"b\":[{\"y\":2,\"z\":1}]}}", withUnsortedMemo);
    }
}
