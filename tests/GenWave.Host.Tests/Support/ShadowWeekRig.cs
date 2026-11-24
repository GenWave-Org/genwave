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
public sealed class ShadowWeekRig(bool recordRowsInMemory = true, bool ruleBookThrows = false, string? startingMode = null, string? reply = null)
{
    /// <summary>A line the rules flag as a repeat that today's code never sees: the row sits only in the hour-window read.</summary>
    public const string RepeatedLine = "Settle in for a golden hour groove from the vault, folks.";

    static readonly DateTimeOffset WrittenAt = new(2026, 11, 9, 20, 0, 0, TimeSpan.FromHours(-6));

    const long PersonaId = 7;
    const string EarlierLine = "Here comes a golden hour groove from the vault tonight.";

    readonly FakeHttpMessageHandler llm = new((_, _) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { choices = new[] { new { message = new { content = reply ?? RepeatedLine } } } }),
        }));

    internal CapturingLoggerProvider Logs { get; } = new();

    internal RecordingTalkCheckLog CheckLog { get; } = new();

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
        services.AddSingleton<IUtteranceStore>(new SeededUtteranceStore(recent: [], inWindow: [earlier]));

        if (recordRowsInMemory)
        {
            services.RemoveAll<ITalkCheckLog>();
            services.AddSingleton<ITalkCheckLog>(CheckLog);
        }

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
