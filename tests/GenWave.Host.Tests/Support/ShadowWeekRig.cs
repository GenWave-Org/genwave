using System.Net;
using System.Net.Http.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Host.Tests.Fakes;
using GenWave.TalkRules;
using GenWave.Tts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// The shadow-week edges around a booted host (STORY-566/567): the LLM HTTP edge answering one scripted reply, an in-memory utterance
/// store, a Warning-capturing logger, and the rule book wrapped so a spec can count or fault it. Everything between stays the real
/// container: <see cref="ISegmentCopyWriter"/> to <see cref="LlmCopyWriter"/> to the shadow hook. A scenario creates the rig in a
/// field initializer (the base fixture builds the host in its constructor) and passes its services through <see cref="Configure"/>.
/// </summary>
public sealed class ShadowWeekRig(
    bool recordRowsInMemory = true, bool ruleBookThrows = false, string? startingMode = null, string? reply = null,
    bool hostSaidTheEarlierLine = false, string[]? replies = null)
{
    /// <summary>A line the rules flag as a repeat that today's code never sees: the row sits only in the hour-window read.</summary>
    public const string RepeatedLine = "Settle in for a golden hour groove from the vault, folks.";

    /// <summary>A three-line exchange that parses cleanly and repeats nothing the speakers said before.</summary>
    public const string ChatReply =
        "HOST: Good evening, and welcome to the late shift.\nNEIGHBOR: Evening, the kettle is on.\nHOST: Then the night is in good hands.";

    /// <summary>A chat whose truth gate drops it under today's code: the host names a real-world frequency.</summary>
    public const string ChatNamingAFrequency =
        "HOST: Good evening, you are tuned to 98.7 FM.\nNEIGHBOR: Evening, the kettle is on.\nHOST: Then the night is in good hands.";

    /// <summary>A chat whose first host line repeats <see cref="EarlierHostLine"/> and keeps a fresh sentence, so a trim saves it.</summary>
    public const string ChatRepeatingOneSentence =
        "HOST: " + EarlierHostLine + " Welcome to the late shift.\nNEIGHBOR: Evening, the kettle is on.\nHOST: Then the night is in good hands.";

    /// <summary>A chat whose first host line is wholly the earlier line, so a trim empties it and the writer re-asks.</summary>
    public const string ChatRepeatingAWholeLine =
        "HOST: " + EarlierHostLine + "\nNEIGHBOR: Evening, the kettle is on.\nHOST: Then the night is in good hands.";

    /// <summary>The line the host is seeded as having said before, when <c>hostSaidTheEarlierLine</c> is set.</summary>
    public const string EarlierHostLine = "Here comes a golden hour groove from the vault tonight.";

    /// <summary>What <see cref="ChatReply"/> airs: its lines without the speaker tags, joined as <see cref="WriteChatAsync"/> joins them.</summary>
    public const string ChatAired =
        "Good evening, and welcome to the late shift.\nEvening, the kettle is on.\nThen the night is in good hands.";

    /// <summary>The lengths of <see cref="ChatReply"/>'s three lines, as the parser measures them.</summary>
    public static readonly IReadOnlyList<int> ChatLineChars = [44, 26, 32];

    /// <summary>The phone number the ad sponsor has on file.</summary>
    public const string SponsorPhone = "812-555-0199";

    /// <summary>An ad reply that says a number which is not the sponsor's, so the phone rewrite changes it.</summary>
    public const string AdSayingAStrayNumber =
        "ANNOUNCER: Cravin's Diner has the best pie in town. Call 406-555-0100 today.";

    /// <summary>What <see cref="AdSayingAStrayNumber"/> airs once the phone rewrite put the sponsor's own number in.</summary>
    public const string AdAired =
        "ANNOUNCER: Cravin's Diner has the best pie in town. Call " + SponsorPhone + " today.";

    /// <summary>An ad reply that says the sponsor's own number, so the phone rewrite leaves it alone.</summary>
    public const string AdSayingTheSponsorsNumber =
        "ANNOUNCER: Cravin's Diner has the best pie in town. Call " + SponsorPhone + " today.";

    // Alberta keeps -06:00 all year, so a fixed-offset zone matches the local time (America/Edmonton would not in summer).
    static readonly TimeZoneInfo StationZone = TimeZoneInfo.CreateCustomTimeZone("station-minus-6", TimeSpan.FromHours(-6), "Station UTC-6", "Station UTC-6");

    static readonly DateTimeOffset WrittenAt = new(2026, 11, 9, 20, 0, 0, TimeSpan.FromHours(-6));

    const long PersonaId = 7;
    const string EarlierLine = EarlierHostLine;

    FakeHttpMessageHandler? llmHandler;

    /// <summary>The LLM HTTP edge: answers the Nth completion with <c>replies</c>[N] (the last repeats), else the one <c>reply</c>.</summary>
    FakeHttpMessageHandler llm => llmHandler ??= new((_, _) =>
    {
        var content = replies is { Length: > 0 }
            ? replies[Math.Min(llmHandler?.Requests.Count ?? 1, replies.Length) - 1]
            : reply ?? RepeatedLine;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { choices = new[] { new { message = new { content } } } }),
        });
    });

    internal CapturingLoggerProvider Logs { get; } = new();

    internal RecordingTalkCheckLog CheckLog { get; } = new();

    LogBackedTalkCheckStore? checkStore;

    /// <summary>The check store the host reads through when rows are kept in memory: the rows the shadow hook queued, plus any a spec adds.</summary>
    internal LogBackedTalkCheckStore CheckStore => checkStore ??= new LogBackedTalkCheckStore(CheckLog);

    ObservedRuleBook? observedRuleBook;

    /// <summary>How many times the rule book was asked to judge.</summary>
    public int JudgeCalls => observedRuleBook?.JudgeCalls ?? 0;

    /// <summary>How many completions requests the LLM edge served.</summary>
    public int LlmCalls => llm.Requests.Count;

    /// <summary>The rows the shadow hook queued (empty unless rows are kept in memory).</summary>
    public IReadOnlyList<TalkCheckRecord> Rows => CheckLog.Records;

    /// <summary>True when any Warning or Error logged contains <paramref name="text"/>.</summary>
    internal bool LoggedWarningContaining(string text) =>
        Logs.Messages.Any(message => message.Contains(text, StringComparison.Ordinal));

    public void Configure(IServiceCollection services)
    {
        services.Configure<LlmOptions>(options =>
        {
            options.Endpoint = "http://llm.test";
            options.Model = "test-model";
        });
        if (startingMode is not null)
            services.Configure<TalkRulesOptions>(options => options.Mode = startingMode);

        services.AddHttpClient(LlmCopyWriter.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => llm);
        services.AddLogging(logging => logging.AddProvider(Logs));

        var earlier = new UtteranceEntry(
            1, PersonaId, null, UtteranceKind.Patter, "LeadIn", ShiftKey.Unscheduled(PersonaId, new DateOnly(2026, 11, 9)),
            EarlierLine, false, DateTimeOffset.UnixEpoch, null);
        services.RemoveAll<IUtteranceStore>();
        services.AddSingleton<IUtteranceStore>(new SeededUtteranceStore(recent: hostSaidTheEarlierLine ? [earlier] : [], inWindow: [earlier]));

        if (recordRowsInMemory)
        {
            services.RemoveAll<ITalkCheckLog>();
            services.AddSingleton<ITalkCheckLog>(CheckLog);
            services.RemoveAll<ITalkCheckStore>();
            services.AddSingleton<ITalkCheckStore>(CheckStore);
        }

        services.RemoveAll<IStationClockProvider>();
        services.AddSingleton<IStationClockProvider>(new FakeStationClockProvider(WrittenAt, StationZone));

        // No station database here: every rule runs at its default, as the in-memory stand-in gives.
        services.RemoveAll<ITalkRuleSwitches>();
        services.AddSingleton<ITalkRuleSwitches, InMemoryTalkRuleSwitches>();

        services.RemoveAll<ITalkRuleBook>();
        services.AddSingleton<ITalkRuleBook>(provider =>
            observedRuleBook = new ObservedRuleBook(provider.GetRequiredService<TalkRuleBook>(), ruleBookThrows));
    }

    /// <summary>Writes one on-air context segment through the host's real copy writer and returns what airs.</summary>
    public async Task<string> WriteContextSegmentAsync(IServiceProvider services)
    {
        var request = new SegmentRequest(SegmentKind.ContextSegment, "af_heart", "GenWave", Track: null, WrittenAt, "test-station")
        {
            PersonaId = PersonaId,
            ShiftKeyText = ShiftKey.Unscheduled(PersonaId, new DateOnly(2026, 11, 9)).ToString(),
        };

        var copy = await services.GetRequiredService<ISegmentCopyWriter>().WriteAsync(request, CancellationToken.None);
        return copy.Text;
    }

    /// <summary>Writes one crosstalk exchange through the host's real script writer and returns the aired lines joined, empty when it is discarded.</summary>
    public async Task<string> WriteChatAsync(IServiceProvider services)
    {
        var request = new CrosstalkExchangeRequest(
            Card("Flip"), Card("Tony"), "GenWave", ShowName: null, Daypart: null, WrittenAt,
            HostPersonaId: PersonaId, NeighborPersonaId: PersonaId + 1);

        var result = await services.GetRequiredService<CrosstalkScriptWriter>().WriteExchangeAsync(request, CancellationToken.None);
        return result is CrosstalkWriteResult.Accepted accepted
            ? string.Join('\n', accepted.Script.Lines.Select(line => line.Text))
            : string.Empty;
    }

    /// <summary>
    /// Writes one ad script through the host's real ad writer and returns the aired script, empty when nothing airs. The
    /// sponsor has <see cref="SponsorPhone"/> on file; <paramref name="validatorRefuses"/> makes the caller's validator refuse
    /// every draft, so the writer asks once more and then gives up.
    /// </summary>
    public async Task<string> WriteAdAsync(IServiceProvider services, bool validatorRefuses = false)
    {
        var request = new AdScriptWriteRequest(
            "Cravin's Diner", null, null, 30, AudiencePosture.Everyone, 450, 0.4, Phone: SponsorPhone, IsPackOwned: false);
        Func<string, AdScriptValidationOutcome> validate = _ => validatorRefuses
            ? new AdScriptValidationOutcome.Refused("duration", "the script runs long")
            : new AdScriptValidationOutcome.Accepted();

        var result = await services.GetRequiredService<AdScriptWriter>().WriteAsync(request, validate, CancellationToken.None);
        return result is AdScriptWriteResult.Success success ? success.Script : string.Empty;
    }

    static PersonaCard Card(string name) =>
        new(PersonaCard.CurrentSchemaVersion, name, "", "Soul line.", [], new VoiceSpec("kokoro", "af_heart", 1.0, "en"), 0.0, [], []);

    /// <summary>Writes one on-air lead-in through the host's real copy writer and returns what airs.</summary>
    public async Task<string> WriteLeadInAsync(IServiceProvider services)
    {
        var request = new SegmentRequest(
            SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            WrittenAt, "test-station")
        {
            PersonaId = PersonaId,
            ShiftKeyText = ShiftKey.Unscheduled(PersonaId, new DateOnly(2026, 11, 9)).ToString(),
        };

        var copy = await services.GetRequiredService<ISegmentCopyWriter>().WriteAsync(request, CancellationToken.None);
        return copy.Text;
    }
}
