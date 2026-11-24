// STORY-566 — Every attempt is recorded and can be replayed (SPEC F220.7 + F220.29 · PLAN T780)
//
// BDD specification — xUnit. The DJ-line snapshot source: pure, built from exactly the inputs the writer already
// read, the same whatever is switched on (F220.7). Not wired into the writer yet.

namespace GenWave.Tts.Tests.Specs;

using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Memory;

public static class FeatureDjLineSnapshot
{
    const long PersonaId = 7;
    static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 30, 0, TimeSpan.FromHours(-6));

    sealed class PostureProbe(AudiencePosture posture) : IAudiencePostureProvider
    {
        public AudiencePosture Current => posture;
    }

    sealed class CatchphraseLimitsProbe : ITalkRule
    {
        public string Id => CatchphraseLimitsMemo.RuleId;

        public TalkKinds CanCover => TalkKinds.DjLine;

        public TalkKinds OnByDefault => TalkKinds.DjLine;

        public TalkRuleDescription Describe() => new("Catchphrase limits", "-", "-", "-");
    }

    static SegmentRequest Request(bool withTrack = true) =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            withTrack ? new MediaItem("m1", "/media/a.mp3", "Song A", default, "Artist A", Album: "Album A", Year: 1984) : null,
            Now, "test-station")
        {
            PersonaId = PersonaId,
            CrossingTrackTitle = "Song B",
            CrossingTrackArtist = "Artist B",
        };

    static PersonaCard Card() =>
        new(PersonaCard.CurrentSchemaVersion, "Flip", "", "", [], new VoiceSpec("", "af_nova", 1.0, "en"), 0, [], [])
        {
            Tagline = "good grief",
            Quirks = ["Says \"stay golden\" now and then", "Greets the weather", "Mutters \"oh boy\" at traffic"],
            QuirkRules = [new QuirkRule("Says \"stay golden\" now and then", MaxPerShift: 1), new QuirkRule("Greets the weather")],
        };

    static LegacyCatchphraseResult Legacy() =>
        LegacyCatchphraseResult.From(
            Card(),
            new QuirkSelection(["Greets the weather"], []),
            [new TaglineStatus("stay golden", true), new TaglineStatus("oh boy", false), new TaglineStatus("good grief", false)]);

    static DjLineSnapshotInput Input(
        LegacyCatchphraseResult? legacy = null,
        bool withTrack = true,
        IReadOnlyList<UtteranceEntry>? newest = null,
        IReadOnlyList<UtteranceEntry>? taglineWindow = null,
        IReadOnlyList<UtteranceEntry>? lastHour = null) =>
        new(Request(withTrack), 2, "Next up [laughs] Song A, stay golden.", PersonaId, "Flip", Card(),
            "It is snowing in Calgary.\nYou once said you hated rain.",
            "Say hi to Dean.", "America/Edmonton",
            newest ?? [], taglineWindow ?? [], lastHour ?? [], legacy);

    static UtteranceEntry Line(long id, string text, TimeSpan ago, bool fallback = false) =>
        new(id, PersonaId, null, UtteranceKind.Patter, "LeadIn",
            ShiftKey.Scheduled(PersonaId, DayOfWeek.Sunday, 360, new DateOnly(2026, 11, 22)),
            text, fallback, Now - ago, null);

    sealed class RecordingLedger : ITaglineLedger
    {
        public PersonaCard? SeenCard { get; private set; }

        public IReadOnlyList<TaglineStatus> Evaluate(
            PersonaCard card, IReadOnlyList<UtteranceEntry> recent, IReadOnlyList<string> spentShiftQuirks, DateTimeOffset now)
        {
            SeenCard = card;
            return new TaglineLedger().Evaluate(card, recent, spentShiftQuirks, now);
        }
    }

    static TalkSnapshot Build(DjLineSnapshotInput input, ITaglineLedger? ledger = null) =>
        new DjLineSnapshotSource(new PostureProbe(AudiencePosture.Mature), ledger ?? new TaglineLedger()).BuildAsync(input, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();

    // Feature: the writer's inputs land on the snapshot
    public sealed class ScenarioTheWritersInputsLandOnTheSnapshot
    {
        readonly TalkSnapshot snapshot = Build(Input());

        [Fact]
        public void TheSnapshotIsADjLine() => Assert.Equal(TalkKinds.DjLine, snapshot.Kind);

        [Fact]
        public void TheSegmentKindIsTheRequestsKind() => Assert.Equal("LeadIn", snapshot.SegmentKind);

        [Fact]
        public void TheAttemptIsCarried() => Assert.Equal(2, snapshot.Attempt);

        [Fact]
        public void TheLineHasItsSoundTagsStripped() =>
            Assert.Equal(new TalkLine("Flip", "Next up Song A, stay golden."), Assert.Single(snapshot.Lines));

        [Fact]
        public void TheFactsIncludeTheCallbackFacts() =>
            Assert.Equal(["It is snowing in Calgary.", "You once said you hated rain."], snapshot.Facts);

        [Fact]
        public void TheOwnerCoreIsCarried() => Assert.Equal("Say hi to Dean.", snapshot.OwnerMessageCore);

        [Fact]
        public void TheStationLocalNowIsCarried() => Assert.Equal(Now, snapshot.LocalNow);

        [Fact]
        public void TheTimezoneIsCarried() => Assert.Equal("America/Edmonton", snapshot.Timezone);

        [Fact]
        public void TheTrackAndCrossingTrackAreCarried() =>
            Assert.Equal(
                new TalkTrack("Song A", "Artist A", "Album A", 1984, new TalkTrack("Song B", "Artist B", null, null, null)),
                snapshot.Track);

        [Fact]
        public void TheStationCarriesTheNameAndPosture() =>
            Assert.Equal(new TalkStation("GenWave", "Mature"), snapshot.Station);
    }

    static readonly TalkList<string> ExpectedCardPhrases = new(
        ["stay golden", "oh boy", "good grief", "Says \"stay golden\" now and then", "Greets the weather"]);

    // Feature: card phrases come from the card, the same whatever is switched on
    public sealed class ScenarioTheSpeakerCarriesCardPhrases
    {
        readonly TalkSnapshot snapshot = Build(Input());

        [Fact]
        public void TaglineSpansAndRawQuirkRuleTextsAreCarried() =>
            Assert.Equal(new TalkSpeaker(PersonaId, "Flip", ExpectedCardPhrases), Assert.Single(snapshot.Speakers));
    }

    // Feature: card phrases do not depend on the shadow result
    public sealed class ScenarioCardPhrasesIgnoreTheLegacyResult
    {
        [Fact]
        public void PhrasesAreEqualWithAndWithoutLegacy() =>
            Assert.Equal(Build(Input()).Speakers, Build(Input(Legacy())).Speakers);
    }

    // Feature: the source reads the tagline spans from the ledger
    public sealed class ScenarioTheSourceAsksTheLedgerAboutTheCard
    {
        readonly RecordingLedger ledger = new();

        public ScenarioTheSourceAsksTheLedgerAboutTheCard() => Build(Input(), ledger);

        [Fact]
        public void TheLedgerWasGivenTheCard() => Assert.Equal(Card().Quirks, ledger.SeenCard?.Quirks);
    }

    // Feature: with no ledger registered the card carries no tagline spans (T713 convention; T780 carried note)
    public sealed class ScenarioNoLedgerMeansNoTaglineSpans
    {
        readonly TalkSnapshot snapshot =
            new DjLineSnapshotSource(new PostureProbe(AudiencePosture.Mature)).BuildAsync(Input(), CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();

        [Fact]
        public void TheSpeakerCarriesOnlyTheRawQuirkRuleTexts() =>
            Assert.Equal(
                new TalkList<string>(["Says \"stay golden\" now and then", "Greets the weather"]),
                Assert.Single(snapshot.Speakers).CardPhrases);
    }

    // Feature: recent lines hold every row the writer read, so each rule picks its own window
    public sealed class ScenarioRecentLinesHoldEveryRowTheWriterRead
    {
        readonly TalkSnapshot snapshot = Build(Input(
            newest: [Line(10, "Template line", TimeSpan.FromMinutes(2), fallback: true), Line(9, "Newest real", TimeSpan.FromMinutes(3))],
            taglineWindow: [Line(9, "Newest real", TimeSpan.FromMinutes(3)), Line(8, "Window line", TimeSpan.FromHours(5))],
            lastHour: [Line(7, "Hour line", TimeSpan.FromMinutes(30)), Line(10, "Template line", TimeSpan.FromMinutes(2), fallback: true)]));

        [Fact]
        public void TheUnionIsNewestFirstWithEachRowOnce() =>
            Assert.Equal(
                ["Template line", "Newest real", "Hour line", "Window line"],
                snapshot.RecentLines.Select(l => l.Text).ToList());

        [Fact]
        public void AFallbackAmongTheNewestRowsIsPresentAndFlagged() =>
            Assert.True(Assert.Single(snapshot.RecentLines, l => l.Text == "Template line").Fallback);

        [Fact]
        public void ARealLineIsNotFlaggedFallback() =>
            Assert.False(Assert.Single(snapshot.RecentLines, l => l.Text == "Newest real").Fallback);

        [Fact]
        public void EachLineCarriesWhenItWasWritten() =>
            Assert.Equal(Now - TimeSpan.FromMinutes(2), snapshot.RecentLines[0].WrittenAt);
    }

    // Feature: shadow leaves the catchphrase memo
    public sealed class ScenarioShadowLeavesTheCatchphraseMemo
    {
        readonly JsonElement memo =
            Build(Input(Legacy())).MemoOf(new CatchphraseLimitsProbe()) ?? throw new InvalidOperationException("No memo.");

        static List<string> Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString() ?? "").ToList();

        [Fact]
        public void TheOfferedQuirksAreRecorded() => Assert.Equal(["Greets the weather"], Strings(memo.GetProperty("offered")));

        [Fact]
        public void TheAllowedTaglinesAreRecorded() => Assert.Equal(["stay golden"], Strings(memo.GetProperty("allowed")));

        [Fact]
        public void TheUnavailableTaglinesAreRecorded() =>
            Assert.Equal(["oh boy", "good grief"], Strings(memo.GetProperty("unavailable")));

        [Fact]
        public void TheSpentCappedRulesAreRecorded() =>
            Assert.Equal(["Says \"stay golden\" now and then"], Strings(memo.GetProperty("spent")));
    }

    // Feature: the crossing track survives without a current track
    public sealed class ScenarioACrossingTrackWithNoCurrentTrack
    {
        readonly TalkSnapshot snapshot = Build(Input(withTrack: false));

        [Fact]
        public void TheCrossingTitleIsPresent() => Assert.Equal("Song B", snapshot.Track?.Crossing?.Title);
    }

    // Feature: no legacy result, no memo
    public sealed class ScenarioNoLegacyResultLeavesNoMemo
    {
        readonly TalkSnapshot snapshot = Build(Input());

        [Fact]
        public void TheCatchphraseMemoIsAbsent() => Assert.Null(snapshot.MemoOf(new CatchphraseLimitsProbe()));
    }

    // Feature: the same snapshot for the same input
    public sealed class ScenarioTheSameInputBuildsTheSameSnapshot
    {
        [Fact]
        public void TwoBuildsAreEqual() =>
            Assert.Equal(
                Build(Input(Legacy(), newest: [Line(1, "Earlier line", TimeSpan.FromMinutes(20))])),
                Build(Input(Legacy(), newest: [Line(1, "Earlier line", TimeSpan.FromMinutes(20))])));
    }

    // Feature: the snapshot is storable
    public sealed class ScenarioTheSnapshotRoundTrips
    {
        readonly TalkSnapshot snapshot = Build(Input(Legacy(), newest: [Line(1, "Template", TimeSpan.FromMinutes(20), fallback: true)]));

        [Fact]
        public void ItEqualsTheOriginalAfterTheJsonCodec() =>
            Assert.Equal(snapshot, TalkSnapshotJson.Deserialize(TalkSnapshotJson.Serialize(snapshot)));
    }

    sealed class UnusedSnapshotSource : ITalkSnapshotSource<DjLineSnapshotInput>
    {
        public ValueTask<TalkSnapshot> BuildAsync(DjLineSnapshotInput input, CancellationToken ct) =>
            throw new InvalidOperationException("The snapshot source is never reached when the input cannot be built.");
    }

    sealed class UnusedRuleBook : ITalkRuleBook
    {
        public ValueTask<TalkShaping> ShapeAsync(TalkSnapshot before, CancellationToken ct) => throw new InvalidOperationException();

        public ValueTask<TalkList<RuleVerdict>> JudgeAsync(TalkSnapshot after, CancellationToken ct) => throw new InvalidOperationException();

        public ValueTask<TalkList<string>> AcceptedAsync(TalkSnapshot accepted, CancellationToken ct) => throw new InvalidOperationException();
    }

    sealed class SilentCheckLog : ITalkCheckLog
    {
        public bool TryRecord(TalkCheckRecord record) => true;
    }

    // Feature: building the shadow input is inside the fault boundary (T788, F220.19)
    public sealed class ScenarioTheInputCannotBeBuilt
    {
        [Fact]
        public async Task TheCheckDoesNotThrow()
        {
            var check = new DjLineShadowCheck(
                new GenWave.Tts.Tests.Fakes.TestOptionsMonitor<TalkRulesOptions>(new TalkRulesOptions { Mode = "shadow" }),
                new UnusedSnapshotSource(), new UnusedRuleBook(), new SilentCheckLog(),
                new GenWave.Tts.Tests.Fakes.CapturingLogger<DjLineShadowCheck>());
            var request = new SegmentRequest(SegmentKind.LeadIn, "af_heart", "GenWave", Track: null, Now, "test-station");

            var thrown = await Record.ExceptionAsync(async () => await check.CheckAsync(
                request, () => throw new InvalidOperationException("input"), "raw", "cleaned", TalkLegacyOutcome.Passed, CancellationToken.None));

            Assert.Null(thrown);
        }
    }

    sealed class RecordingCheckLog : ITalkCheckLog
    {
        public List<TalkCheckRecord> Records { get; } = [];

        public bool TryRecord(TalkCheckRecord record)
        {
            Records.Add(record);
            return true;
        }
    }

    sealed class EmptyRuleBook : ITalkRuleBook
    {
        public ValueTask<TalkShaping> ShapeAsync(TalkSnapshot before, CancellationToken ct) => throw new InvalidOperationException();

        public ValueTask<TalkList<RuleVerdict>> JudgeAsync(TalkSnapshot after, CancellationToken ct) =>
            ValueTask.FromResult(TalkList<RuleVerdict>.Empty);

        public ValueTask<TalkList<string>> AcceptedAsync(TalkSnapshot accepted, CancellationToken ct) =>
            ValueTask.FromResult(TalkList<string>.Empty);
    }

    // Feature: a rejected preview is recorded as what it was, a dropped line (T788, F220.19). WritePreviewAsync never templates.
    public sealed class ScenarioARejectedPreviewIsRecordedAsDropped : IAsyncLifetime
    {
        readonly RecordingCheckLog checkLog = new();

        public async Task InitializeAsync()
        {
            var handler = new GenWave.Tts.Tests.Fakes.FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = "" } } } }),
                    System.Text.Encoding.UTF8, "application/json"),
            }));
            var check = new DjLineShadowCheck(
                new GenWave.Tts.Tests.Fakes.TestOptionsMonitor<TalkRulesOptions>(new TalkRulesOptions { Mode = "shadow" }),
                new DjLineSnapshotSource(new PostureProbe(AudiencePosture.Mature)), new EmptyRuleBook(), checkLog,
                new GenWave.Tts.Tests.Fakes.CapturingLogger<DjLineShadowCheck>());
            var writer = new LlmCopyWriter(
                new TemplateCopyWriter(new PatterTemplateRenderer()),
                new GenWave.Tts.Tests.Fakes.SingleHandlerHttpClientFactory(handler),
                new GenWave.Tts.Tests.Fakes.TestOptionsMonitor<LlmOptions>(new LlmOptions
                {
                    Endpoint = "http://fake-llm.local", Model = "test-model", TimeoutSeconds = 5, MaxCopyChars = 450,
                }),
                new LlmCopyStatusHolder(),
                new GenWave.Tts.Tests.Fakes.FakeActivePersonaAccessor(),
                new GenWave.Tts.Tests.Fakes.CapturingLogger<LlmCopyWriter>(),
                TimeProvider.System,
                new LlmCallRecorder(
                    new LlmCallRing(new GenWave.Tts.Tests.Fakes.TestOptionsMonitor<LlmOptions>(new LlmOptions())),
                    new LlmCallCauseCounters(TimeProvider.System)),
                new GenWave.Tts.Tests.Fakes.FakeDegradationModeReader(),
                shadowCheck: check);
            var request = new SegmentRequest(SegmentKind.LeadIn, "af_heart", "GenWave", Track: null, Now, "test-station");

            await writer.WritePreviewAsync(request, personaOverride: null, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ExactlyOneRowIsWrittenAndItIsDropped() =>
            Assert.Equal(TalkLegacyOutcome.Dropped, Assert.Single(checkLog.Records).LegacyOutcome);
    }
}
