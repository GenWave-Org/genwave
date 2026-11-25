using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Core.TalkRules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenWave.Tts;

/// <summary>
/// The rules-mode flow for one two-DJ chat (SPEC F220.10-F220.16, F220.29): the chat twin of <see cref="DjLineRulesFlow"/>. The
/// shapers add their notes to the prompt, the model is asked, the reply is parsed (the fixed clean-up), every judge runs over the
/// same snapshot, and a flag from a switched-on rule earns exactly one re-ask that lists every flagged rule's ask (sorted by rule
/// id). A second flag drops the chat; a chat never falls back to a template. A clean-up failure (a malformed reply) is dropped with
/// no re-ask. Nothing is trimmed. Every attempt writes a <c>talk_check</c> row; a flag that changes the outcome writes a
/// <c>rule-flagged</c> booth row naming the host.
/// </summary>
/// <remarks>
/// <see cref="CrosstalkScriptWriter"/> branches to this once per chat and keeps what it already owns: the HTTP call and the parse
/// (through <see cref="ChatRulesCalls"/>), the ring and the stored lines. The two flows share <see cref="RulesFlowSupport"/> (flags,
/// the broken-rule marks, the check row, the booth row) and differ in the snapshot, the clean-up and the fall-back. As in the DJ
/// flow, the accepted attempt's row waits for <see cref="AcceptedAsync"/> so a recorder that threw is on it (F220.9).
/// </remarks>
public sealed class ChatRulesFlow(
    IOptionsMonitor<TalkRulesOptions> options,
    ITalkSnapshotSource<ChatSnapshotInput> snapshots,
    ITalkRuleBook ruleBook,
    ITalkCheckLog checkLog,
    IStationEventSink events,
    ILogger<ChatRulesFlow> logger)
{
    readonly RulesFlowSupport support = new(ruleBook, checkLog, events, logger);

    /// <summary>True when the live mode is rules (F220.10); see <see cref="ShadowCheckSupport.IsRules"/>. Never throws.</summary>
    public bool IsOn() => ShadowCheckSupport.IsRules(options, logger);

    /// <summary>
    /// Shape, ask, parse, judge, and re-ask once; see the type remarks. <paramref name="input"/> is the snapshot input with no script
    /// yet. <paramref name="compose"/> builds both prompts from the shaping; each call is handed the prompt it asks.
    /// Faults from the model call propagate to the writer.
    /// </summary>
    internal async Task<ChatRulesResult> RunAsync(
        ChatSnapshotInput input, Func<TalkShaping, BreakPrompt> compose, ChatRulesCalls calls, CancellationToken ct)
    {
        var before = await snapshots.BuildAsync(input, ct);
        var shaping = await ruleBook.ShapeAsync(before with { Memos = TalkMemos.Empty }, ct);
        var prompt = compose(shaping);

        var first = await AttemptAsync(input, shaping, 1, calls.Ask, prompt, ct);
        if (first.Result is { } decided1)
            return decided1;

        // Flagged: one re-ask naming every flagged rule's ask, sorted by rule id (F220.13). The caught call keeps its own ring entry.
        calls.RecordCaught(first.Raw);
        PublishFlags(input, first.Flags, RuleFlagOutcome.ReAsked);
        var reAsk = LlmPromptBuilder.BuildRuleReAsk(prompt.User, first.Flags.Select(f => f.Flag.Ask));
        var second = await AttemptAsync(input, shaping, 2, calls.Ask, prompt with { User = reAsk }, ct);
        if (second.Result is { } decided2)
            return decided2;

        // Still flagged: the chat is dropped (F220.14).
        PublishFlags(input, second.Flags, RuleFlagOutcome.Dropped);
        var names = second.Flags.Select(f => support.NameOf(f.Verdict.RuleId));
        return new ChatRulesResult(
            second.Raw,
            new CrosstalkWriteResult.Discarded($"the re-ask was flagged again ({string.Join(", ", names)})", LlmCallCause.RuleFlagged),
            null);
    }

    /// <summary>
    /// Tells the recording rules about the accepted chat (F220.15; a chat has no utterance, so the snapshot's id stays null), then
    /// writes the accepted attempt's row with any recorder that threw marked "Could not record" on its verdict (F220.9). Never
    /// throws except for the caller's own cancellation: accepted talk stays accepted.
    /// </summary>
    internal async ValueTask AcceptedAsync(ChatAcceptedTalk accepted, CancellationToken ct)
    {
        await support.RecordAcceptedAsync(
            accepted.Request.HostPersonaId, accepted.Raw, accepted.Cleaned, accepted.Snapshot, accepted.Verdicts, ct);
    }

    private async Task<Attempt> AttemptAsync(
        ChatSnapshotInput input, TalkShaping shaping, int attempt, Func<int, BreakPrompt, Task<ChatReply>> ask, BreakPrompt prompt,
        CancellationToken ct)
    {
        var reply = await ask(attempt, prompt);
        var request = input.Request;

        if (reply.Result is not CrosstalkWriteResult.Accepted accepted)
        {
            // Clean-up failed (F220.11): no whole script to judge, so no rule runs and nothing is re-asked; the attempt is still
            // recorded (F220.29), with the script the parser had built when it has one.
            var discarded = reply.Result as CrosstalkWriteResult.Discarded;
            var snapshot = await SnapshotAsync(input, shaping, attempt, discarded?.Parsed ?? new CrosstalkAiredScript([]), ct);
            support.Write(request.HostPersonaId, reply.Raw, null, snapshot, TalkList<RuleVerdict>.Empty, TalkOutcome.Dropped);
            return new Attempt(new ChatRulesResult(reply.Raw, reply.Result, null), [], reply.Raw);
        }

        var cleaned = string.Join('\n', accepted.Script.Lines.Select(CrosstalkScriptParser.DescribeLine));
        var judged = await SnapshotAsync(input, shaping, attempt, accepted.Script, ct);
        var verdicts = RulesFlowSupport.Mark(await ruleBook.JudgeAsync(judged, ct), shaping.CouldNotShape, TalkRuleReasons.CouldNotShape);
        var flags = RulesFlowSupport.FlagsOf(verdicts);
        if (flags.Count == 0)
        {
            // The row waits for AcceptedAsync, so the recorders' breakage can be on it.
            return new Attempt(
                new ChatRulesResult(reply.Raw, accepted, new ChatAcceptedTalk(request, reply.Raw, cleaned, judged, verdicts)), [], reply.Raw);
        }

        support.Write(
            request.HostPersonaId, reply.Raw, cleaned, judged, verdicts, attempt == 1 ? TalkOutcome.ReAsked : TalkOutcome.Dropped);
        return new Attempt(null, flags, reply.Raw);
    }

    private async ValueTask<TalkSnapshot> SnapshotAsync(
        ChatSnapshotInput input, TalkShaping shaping, int attempt, CrosstalkAiredScript script, CancellationToken ct) =>
        (await snapshots.BuildAsync(input with { Attempt = attempt, Script = script }, ct)) with { Memos = shaping.Memos };

    /// <summary>A chat names its host: the flags' evidence is free text, so no flagged line can be told from it.</summary>
    private void PublishFlags(ChatSnapshotInput input, IReadOnlyList<FlaggedRule> flags, RuleFlagOutcome outcome) =>
        support.PublishFlags(input.Request.HostPersonaId, input.Request.HostCard.Name, RuleFlagTalk.Chat, flags, outcome);

    private sealed record Attempt(ChatRulesResult? Result, IReadOnlyList<FlaggedRule> Flags, string Raw);
}
