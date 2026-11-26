namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Memory;
using GenWave.TalkRules;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

/// <summary>
/// STORY-570 chat arrangement: a real <see cref="CrosstalkScriptWriter"/> in rules mode over a real <see cref="TalkRuleBook"/> of
/// the given rules, a scripted model, and recording doubles for the check log and the station events. The chat twin of
/// <see cref="RulesSpecHarness"/>.
/// </summary>
public sealed class ChatRulesSpecHarness
{
    public const long HostId = 7;
    public const long NeighborId = 8;

    /// <summary>A three-line exchange that parses cleanly.</summary>
    public const string Clean =
        "HOST: Good evening, and welcome to the late shift.\nNEIGHBOR: Evening, the kettle is on.\nHOST: Then the night is in good hands.";

    /// <summary>A second clean exchange, for a re-ask's reply.</summary>
    public const string CleanAgain =
        "HOST: Welcome back, the records are warm tonight.\nNEIGHBOR: And the lights are low, just right.\nHOST: Settle in, friends, we have all night.";

    /// <summary>What <see cref="Clean"/> airs: its lines without the tags.</summary>
    public const string CleanAired =
        "Good evening, and welcome to the late shift.\nEvening, the kettle is on.\nThen the night is in good hands.";

    public ChatRulesSpecHarness(
        string[] replies, IReadOnlyList<ITalkRule> rules, string mode = "rules", IOptionsMonitor<TalkRulesOptions>? modeMonitor = null,
        IUtteranceStore? utterances = null, ITalkSnapshotSource<ChatSnapshotInput>? snapshots = null)
    {
        var ruleBookLogger = new CapturingLogger<TalkRuleBook>();
        RuleBookLog = ruleBookLogger;
        var flow = new ChatRulesFlow(
            modeMonitor ?? new TestOptionsMonitor<TalkRulesOptions>(new TalkRulesOptions { Mode = mode }),
            snapshots ?? new ChatSnapshotSource(new AlwaysEveryone()),
            new TalkRuleBook(rules, new InMemoryTalkRuleSwitches(), ruleBookLogger),
            CheckLog, Events, NullLogger<ChatRulesFlow>.Instance);
        Writer = new CrosstalkScriptWriter(
            QuirkSpecHarness.ScriptedHttp(UserPrompts, replies, SystemPrompts),
            new TestOptionsMonitor<LlmOptions>(new LlmOptions { Endpoint = "http://fake-llm.local", Model = "m", TimeoutSeconds = 5, MaxCopyChars = 450 }),
            new TestOptionsMonitor<CrosstalkOptions>(new CrosstalkOptions()),
            new LlmCallRecorder(Ring, Counters),
            new FakeDegradationModeReader(),
            NullLogger<CrosstalkScriptWriter>.Instance,
            TimeProvider.System,
            repetitionCheck: new RepetitionCheck(),
            utteranceStore: utterances ?? Utterances,
            events: Events,
            sentenceTrimmer: new SentenceTrimmer(),
            rulesFlow: flow);
    }

    public CrosstalkScriptWriter Writer { get; }

    public List<string> UserPrompts { get; } = [];

    public List<string> SystemPrompts { get; } = [];

    public RecordingTalkCheckLog CheckLog { get; } = new();

    public RecordingStationEventSink Events { get; } = new();

    public RecordingUtteranceStore Utterances { get; } = new();

    public LlmCallRing Ring { get; } = new(new TestOptionsMonitor<LlmOptions>(new LlmOptions()));

    public LlmCallCauseCounters Counters { get; } = new(TimeProvider.System);

    public CapturingLogger<TalkRuleBook> RuleBookLog { get; }

    public static PersonaCard Card(string name) =>
        new(PersonaCard.CurrentSchemaVersion, name, "", "Soul line.", [],
            new VoiceSpec("kokoro", "af_heart", 1.0, "en"), 0.0, [], []);

    public Task<CrosstalkWriteResult> WriteAsync() =>
        Writer.WriteExchangeAsync(
            new CrosstalkExchangeRequest(
                Card("Nova"), Card("Rex"), "GenWave", ShowName: "Night Shift", Daypart: null,
                StationLocalNow: new DateTimeOffset(2026, 11, 9, 22, 0, 0, TimeSpan.Zero),
                HostPersonaId: HostId, NeighborPersonaId: NeighborId, ShowId: 3),
            CancellationToken.None);

    /// <summary>The aired lines joined as the specs compare them, empty when the chat is not accepted.</summary>
    public static string AiredOf(CrosstalkWriteResult result) =>
        result is CrosstalkWriteResult.Accepted accepted ? string.Join('\n', accepted.Script.Lines.Select(l => l.Text)) : string.Empty;

    sealed class AlwaysEveryone : IAudiencePostureProvider
    {
        public AudiencePosture Current => AudiencePosture.Everyone;
    }
}
