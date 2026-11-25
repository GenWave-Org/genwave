namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Memory;
using GenWave.TalkRules;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

/// <summary>An <see cref="ITalkCheckLog"/> that keeps every record it is handed.</summary>
public sealed class RecordingTalkCheckLog : ITalkCheckLog
{
    public List<TalkCheckRecord> Records { get; } = [];

    public bool TryRecord(TalkCheckRecord record)
    {
        Records.Add(record);
        return true;
    }
}

/// <summary>A judging rule whose flag is scripted per snapshot; it also keeps the snapshots it judged.</summary>
public sealed class ScriptedJudgingRule(string id, string name, Func<TalkSnapshot, RuleFlag?> judge) : IJudgingTalkRule
{
    public List<TalkSnapshot> Judged { get; } = [];

    public string Id => id;

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public TalkRuleDescription Describe() => new(name, "-", "-", "-");

    public RuleFlag? Judge(TalkSnapshot after)
    {
        Judged.Add(after);
        return judge(after);
    }
}

/// <summary>A shaping and recording rule that keeps what it sees, and can be told to throw.</summary>
public sealed class ScriptedShapingRule(string id, string name, string? note = null, bool shapeThrows = false, bool recordThrows = false)
    : IShapingTalkRule, IRecordingTalkRule
{
    public List<TalkSnapshot> Shaped { get; } = [];

    public List<TalkSnapshot> Accepted { get; } = [];

    public string Id => id;

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public TalkRuleDescription Describe() => new(name, "-", "-", "-");

    public ValueTask<RuleShaping?> ShapeAsync(TalkSnapshot before, CancellationToken ct)
    {
        Shaped.Add(before);
        return shapeThrows
            ? throw new InvalidOperationException("shaper down")
            : ValueTask.FromResult<RuleShaping?>(note is null ? null : new RuleShaping(note, null));
    }

    public ValueTask AcceptedAsync(TalkSnapshot accepted, CancellationToken ct)
    {
        Accepted.Add(accepted);
        return recordThrows ? throw new InvalidOperationException("recorder down") : ValueTask.CompletedTask;
    }
}

/// <summary>
/// STORY-570 writer arrangement: a real <see cref="LlmCopyWriter"/> in rules mode over a real <see cref="TalkRuleBook"/> of
/// the given rules, a scripted model, and recording doubles for the utterance store, the check log and the station events.
/// </summary>
public sealed class RulesSpecHarness
{
    public RulesSpecHarness(
        string[] replies, IReadOnlyList<ITalkRule> rules, PersonaCard? card = null, SegmentKind kind = SegmentKind.LeadIn,
        ITalkRuleSwitches? switches = null, IMemoryRecallSource? memoryRecall = null, BreakCallbacks? callbacks = null,
        string mode = "rules", SegmentRequest? request = null,
        IOptionsMonitor<TalkRulesOptions>? modeMonitor = null, TimeProvider? time = null)
    {
        Rules = rules;
        var ruleBookLogger = new CapturingLogger<TalkRuleBook>();
        RuleBookLog = ruleBookLogger;
        var flow = new DjLineRulesFlow(
            modeMonitor ?? new TestOptionsMonitor<TalkRulesOptions>(new TalkRulesOptions { Mode = mode }),
            new DjLineSnapshotSource(new AlwaysEveryone(), new TaglineLedger()),
            new TalkRuleBook(rules, switches ?? new InMemoryTalkRuleSwitches(), ruleBookLogger),
            CheckLog, Events, FlowLog);
        Request = request
            ?? QuirkSpecHarness.Request(ShiftKey.Unscheduled(QuirkSpecHarness.PersonaId, new DateOnly(2026, 11, 16)).ToString())
                with { Kind = kind };
        Writer = new LlmCopyWriter(
            new TemplateCopyWriter(new PatterTemplateRenderer()),
            QuirkSpecHarness.ScriptedHttp(UserPrompts, replies, SystemPrompts),
            new TestOptionsMonitor<LlmOptions>(new LlmOptions { Endpoint = "http://fake-llm.local", Model = "m", TimeoutSeconds = 5, MaxCopyChars = 450 }),
            new LlmCopyStatusHolder(), new FakeActivePersonaAccessor { Card = card ?? QuirkSpecHarness.Card([], null) },
            NullLogger<LlmCopyWriter>.Instance, time ?? TimeProvider.System, Recorder, new FakeDegradationModeReader(),
            utteranceStore: Utterances, quirkSelector: new NoQuirks(), events: Events, memoryRecall: memoryRecall,
            callbacks: callbacks, contextWindow: new FixedContextWindow(MemoryBudget.Standard), rulesFlow: flow);
    }

    public IReadOnlyList<ITalkRule> Rules { get; }

    public SegmentRequest Request { get; }

    public LlmCopyWriter Writer { get; }

    public List<string> UserPrompts { get; } = [];

    public List<string> SystemPrompts { get; } = [];

    public RecordingTalkCheckLog CheckLog { get; } = new();

    public RecordingStationEventSink Events { get; } = new();

    public RecordingUtteranceStore Utterances { get; } = new();

    public LlmCallCauseCounters Counters { get; } = new(TimeProvider.System);

    LlmCallRecorder Recorder => QuirkSpecHarness.NewRecorder(Counters);

    public CapturingLogger<DjLineRulesFlow> FlowLog { get; } = new();

    public CapturingLogger<TalkRuleBook> RuleBookLog { get; }

    public Task<SegmentCopy> WriteAsync() => Writer.WriteAsync(Request, CancellationToken.None);

    /// <summary>A card with no soul and no quirks: the persona section is absent, so legacy offers no recent lines.</summary>
    public static PersonaCard BareCard() =>
        new(PersonaCard.CurrentSchemaVersion, "DJ Nova", "", "", [], new VoiceSpec("kokoro", "af_heart", 1.0, "en"), 0.0, [], []);

    sealed class AlwaysEveryone : IAudiencePostureProvider
    {
        public AudiencePosture Current => AudiencePosture.Everyone;
    }

    sealed class NoQuirks : IQuirkSelector
    {
        public Task<QuirkSelection> SelectAsync(
            long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct) =>
            Task.FromResult(QuirkSelection.None);
    }
}
