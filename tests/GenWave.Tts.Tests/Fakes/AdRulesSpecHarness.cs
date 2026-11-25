namespace GenWave.Tts.Tests.Fakes;

using GenWave.Ads;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.TalkRules;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

/// <summary>
/// STORY-570 ad arrangement: a real <see cref="AdScriptWriter"/> in rules mode over a real <see cref="AdRulesFlow"/> and
/// <see cref="TalkRuleBook"/> of the given rules, the real <see cref="AdSnapshotSource"/>, a scripted model, and recording doubles
/// for the check log and the station events. The ad twin of <see cref="ChatRulesSpecHarness"/>.
/// </summary>
public sealed class AdRulesSpecHarness
{
    public const string SponsorName = "Cravin's Diner";
    public const string SponsorPhone = "222-0100";

    /// <summary>A two-line spot that parses cleanly and says the sponsor's own number.</summary>
    public const string Clean = "ANNOUNCER: Cravin's Diner has the best pie in town.\nANNOUNCER: Call 222-0100 today.";

    /// <summary>A second clean spot, for a re-ask's reply.</summary>
    public const string CleanAgain = "ANNOUNCER: Cravin's Diner bakes pie all night long.\nANNOUNCER: Call 222-0100 and ask for a slice.";

    /// <summary>A parsing spot that says a number that is not the sponsor's.</summary>
    public const string WrongPhone = "ANNOUNCER: Cravin's Diner has the best pie in town.\nANNOUNCER: Call 222-0199 today.";

    public AdRulesSpecHarness(
        string[] replies, IReadOnlyList<ITalkRule> rules, string mode = "rules", IOptionsMonitor<TalkRulesOptions>? modeMonitor = null,
        RecordingTalkCheckLog? checkLog = null)
    {
        CheckLog = checkLog ?? new RecordingTalkCheckLog();
        var ruleBookLogger = new CapturingLogger<TalkRuleBook>();
        var flow = new AdRulesFlow(
            modeMonitor ?? new TestOptionsMonitor<TalkRulesOptions>(new TalkRulesOptions { Mode = mode }),
            new AdSnapshotSource(new HeuristicEstimator()),
            new TalkRuleBook(rules, new InMemoryTalkRuleSwitches(), ruleBookLogger),
            CheckLog, Events,
            new FakeStationIdentityProvider(new StationIdentity("station-1", "GenWave", "voice-station")),
            new FakeStationClockProvider(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.FromHours(-6))),
            NullLogger<AdRulesFlow>.Instance);
        Writer = new AdScriptWriter(
            QuirkSpecHarness.ScriptedHttp(UserPrompts, replies, SystemPrompts),
            new TestOptionsMonitor<LlmOptions>(new LlmOptions { Endpoint = "http://fake-llm.local", Model = "m", TimeoutSeconds = 5 }),
            new LlmCallRecorder(Ring, Counters),
            new FakeDegradationModeReader(),
            NullLogger<AdScriptWriter>.Instance,
            TimeProvider.System,
            rulesFlow: flow);
    }

    public AdScriptWriter Writer { get; }

    public List<string> UserPrompts { get; } = [];

    public List<string> SystemPrompts { get; } = [];

    public RecordingTalkCheckLog CheckLog { get; }

    public RecordingStationEventSink Events { get; } = new();

    public LlmCallRing Ring { get; } = new(new TestOptionsMonitor<LlmOptions>(new LlmOptions()));

    public LlmCallCauseCounters Counters { get; } = new(TimeProvider.System);

    /// <summary>How many times the legacy validate delegate was called.</summary>
    public int LegacyValidations { get; private set; }

    public static AdScriptWriteRequest Request() =>
        new(SponsorName, "A diner with pie", "warm", 30, AudiencePosture.Everyone, 200, 0.4, Phone: SponsorPhone, IsPackOwned: false);

    public Task<AdScriptWriteResult> WriteAsync() =>
        Writer.WriteAsync(
            Request(),
            _ =>
            {
                LegacyValidations++;
                return new AdScriptValidationOutcome.Accepted();
            },
            CancellationToken.None);

    /// <summary>The aired script, empty when the spot was not written.</summary>
    public static string AiredOf(AdScriptWriteResult result) => result is AdScriptWriteResult.Success success ? success.Script : string.Empty;

    sealed class HeuristicEstimator : IPatterDurationEstimator
    {
        public PatterDurationEstimate Estimate(SegmentKind kind, string? personaName, string voice) =>
            new(TimeSpan.FromSeconds(5), PatterEstimateConfidence.Heuristic);

        public void ObserveRendered(SegmentKind kind, string? personaName, string voice, TimeSpan measured)
        {
        }
    }
}
