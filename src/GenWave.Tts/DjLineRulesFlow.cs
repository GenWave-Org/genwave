using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Core.TalkRules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenWave.Tts;

/// <summary>
/// The rules-mode flow for one DJ line (SPEC F220.10-F220.16, F220.29): the shapers add their notes to the prompt, the model is
/// asked, the reply is cleaned, every judge runs over the same snapshot, and a flag from a switched-on rule earns exactly one
/// re-ask that lists every flagged rule's ask (sorted by rule id). A second flag ends it: template copy, or a dropped line
/// where template copy never airs. Nothing is trimmed. Every attempt writes a <c>talk_check</c> row; a flag that changes the
/// outcome writes a <c>rule-flagged</c> booth row.
/// </summary>
/// <remarks>
/// <see cref="LlmCopyWriter"/> branches to this once per talk and keeps the pieces it already owns: the HTTP call, the
/// clean-up, the call ring (through <see cref="DjLineRulesCalls"/>) and the template fallback. The mode is read fresh per
/// talk, like <see cref="DjLineShadowCheck"/>'s. Accepted talk is handed to the recording rules by
/// <see cref="AcceptedAsync"/> once the writer has the utterance id, so a rule that settles something against the
/// utterance can. The accepted attempt's <c>talk_check</c> row is written there too, after the recorders have run, so a
/// recorder that threw is on the row (F220.9); every other attempt writes its row when it is decided.
/// </remarks>
public sealed class DjLineRulesFlow(
    IOptionsMonitor<TalkRulesOptions> options,
    ITalkSnapshotSource<DjLineSnapshotInput> snapshots,
    ITalkRuleBook ruleBook,
    ITalkCheckLog checkLog,
    IStationEventSink events,
    ILogger<DjLineRulesFlow> logger)
{
    readonly RulesFlowSupport support = new(ruleBook, checkLog, events, logger);

    /// <summary>True when the live mode is rules (F220.10); see <see cref="ShadowCheckSupport.IsRules"/>. Never throws.</summary>
    public bool IsOn() => ShadowCheckSupport.IsRules(options, logger);

    /// <summary>Shape, ask, clean, judge, and re-ask once; see the type remarks. Faults from the model call propagate to the writer.</summary>
    internal async Task<DjLineRulesResult> RunAsync(
        DjLineRulesRender render, Func<TalkShaping, BreakPrompt> compose, DjLineRulesCalls calls, CancellationToken ct)
    {
        var shaping = await ShapeAsync(render, ct);
        var prompt = compose(shaping);

        var first = await AttemptAsync(render, shaping, 1, calls.Ask, prompt.User, calls.Record, ct);
        if (first.Result is { } decided1)
            return decided1;

        // Flagged: one re-ask naming every flagged rule's ask, sorted by rule id (F220.12).
        PublishFlags(render, first.Flags, RuleFlagOutcome.ReAsked);
        var reAsk = LlmPromptBuilder.BuildRuleReAsk(prompt.User, first.Flags.Select(f => f.Flag.Ask));
        var second = await AttemptAsync(render, shaping, 2, calls.Ask, reAsk, calls.Record, ct);
        if (second.Result is { } decided2)
            return decided2;

        // Still flagged: template copy, or dropped where it never airs (F220.14, F220.15).
        var outcome = ShadowCheckSupport.NoTextOutcome(render.Input.Request, render.Input.Preview);
        PublishFlags(render, second.Flags, outcome == TalkLegacyOutcome.Dropped ? RuleFlagOutcome.Dropped : RuleFlagOutcome.Template);
        return new DjLineRulesResult(
            new LlmCopyCleanupResult.RuleRejected(second.Flags.Select(f => support.NameOf(f.Verdict.RuleId)).ToList()), null);
    }

    /// <summary>
    /// Tells the recording rules about accepted talk (F220.13), with <paramref name="utteranceId"/> set on the snapshot (null when
    /// there is no utterance: an announcement, or one that could not be recorded), then writes the accepted attempt's row with any
    /// recorder that threw marked "Could not record" on its verdict (F220.9). Never throws except for the caller's own cancellation:
    /// the book reports a recorder that threw by id with a Warning, and accepted talk stays accepted.
    /// </summary>
    internal async ValueTask AcceptedAsync(DjLineAcceptedTalk accepted, long? utteranceId, CancellationToken ct)
    {
        var snapshot = accepted.Snapshot with { UtteranceId = utteranceId };
        await support.RecordAcceptedAsync(
            accepted.Request.PersonaId, accepted.Raw, accepted.Cleaned, snapshot, accepted.Verdicts, ct);
    }

    /// <summary>
    /// Writes the accepted attempt's row for talk that never airs (a preview) without telling the recording rules: nothing is
    /// settled for a line nobody hears (F220.29: every written attempt is kept in rules mode).
    /// </summary>
    internal void RecordUnaired(DjLineAcceptedTalk accepted) =>
        Write(accepted.Request, accepted.Raw, accepted.Cleaned, accepted.Snapshot, accepted.Verdicts, TalkOutcome.Accepted);

    /// <summary>The shapers' pass, over a snapshot with no line yet. A bare persona is offered no recent lines, as legacy offers none.</summary>
    private async ValueTask<TalkShaping> ShapeAsync(DjLineRulesRender render, CancellationToken ct)
    {
        var input = render.OffersRecentLines
            ? render.Input
            : render.Input with { NewestRows = [], TaglineWindowRows = [], LastHourRows = [] };
        var before = Carry(await snapshots.BuildAsync(input, ct), render, TalkMemos.Empty) with { Lines = TalkList<TalkLine>.Empty };
        return await ruleBook.ShapeAsync(before, ct);
    }

    private async Task<Attempt> AttemptAsync(
        DjLineRulesRender render, TalkShaping shaping, int attempt, Func<int, string, Task<DjLineReply>> ask, string userPrompt,
        Action<DjLineReply, bool> record, CancellationToken ct)
    {
        var reply = await ask(attempt, userPrompt);
        var request = render.Input.Request;
        var cleaned = LlmCopyWriter.TextOf(reply.Cleanup);
        var snapshot = Carry(
            await snapshots.BuildAsync(render.Input with { Attempt = attempt, CleanedText = cleaned ?? string.Empty }, ct),
            render, shaping.Memos);

        if (cleaned is null)
        {
            // Clean-up failed: no line to judge, so no rule runs and nothing is re-asked; the attempt is still recorded (F220.29).
            record(reply, false);
            var outcome = ShadowCheckSupport.OutcomeOf(ShadowCheckSupport.NoTextOutcome(request, render.Input.Preview));
            Write(request, reply.Raw, null, snapshot, TalkList<RuleVerdict>.Empty, outcome);
            return new Attempt(new DjLineRulesResult(reply.Cleanup, null), []);
        }

        var verdicts = RulesFlowSupport.Mark(await ruleBook.JudgeAsync(snapshot, ct), shaping.CouldNotShape, TalkRuleReasons.CouldNotShape);
        var flags = RulesFlowSupport.FlagsOf(verdicts);
        if (flags.Count == 0)
        {
            // The row waits for AcceptedAsync, so the recorders' breakage can be on it.
            record(reply, false);
            return new Attempt(
                new DjLineRulesResult(reply.Cleanup, new DjLineAcceptedTalk(request, reply.Raw, cleaned, snapshot, verdicts)), []);
        }

        record(reply, true);
        var flaggedOutcome = attempt == 1
            ? TalkOutcome.ReAsked
            : ShadowCheckSupport.OutcomeOf(ShadowCheckSupport.NoTextOutcome(request, render.Input.Preview));
        Write(request, reply.Raw, cleaned, snapshot, verdicts, flaggedOutcome);
        return new Attempt(null, flags);
    }

    private void Write(
        SegmentRequest request, string raw, string? cleaned, TalkSnapshot snapshot, TalkList<RuleVerdict> verdicts,
        TalkOutcome outcome) =>
        support.Write(request.PersonaId, raw, cleaned, snapshot, verdicts, outcome);

    private void PublishFlags(DjLineRulesRender render, IReadOnlyList<FlaggedRule> flags, RuleFlagOutcome outcome)
    {
        // No persona, no row to key it on.
        if (render.Input.Request.PersonaId is null)
            return;

        support.PublishFlags(render.Input.Request.PersonaId, render.Input.PersonaName, RuleFlagTalk.Line, flags, outcome);
    }

    private static TalkSnapshot Carry(TalkSnapshot snapshot, DjLineRulesRender render, TalkMemos memos) =>
        snapshot with
        {
            Memos = memos,
            TasteNotes = render.TasteNotes,
            PreviousTasteNotes = render.PreviousTasteNotes,
            ExtraLineTaken = render.ExtraLineTaken,
        };

    private sealed record Attempt(DjLineRulesResult? Result, IReadOnlyList<FlaggedRule> Flags);
}
