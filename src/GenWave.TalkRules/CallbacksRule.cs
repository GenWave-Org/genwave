using System.Text.Json;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Tts;

namespace GenWave.TalkRules;

/// <summary>
/// Callbacks. Once in a while a lead-in or back-announce is asked to work in one memory the DJ has (F218.40). Shape-and-record,
/// the same offer and the same settling as <c>LlmCopyWriter</c> makes today through <see cref="BreakCallbacks"/>:
/// <list type="bullet">
/// <item>Shaping: on an on-air lead-in or back-announce where <see cref="MemoryBreakGate"/> opens memory, <see cref="BreakCallbacks.PlanStrictAsync"/>
/// draws and writes the offer row before the writer is asked (F218.44), and the note is the ask as <see cref="LlmPromptBuilder.BuildCallbackAskLines"/>
/// builds it for the legacy user prompt too. What was offered goes on the <see cref="CallbackOfferMemo"/>. The offer row is written at once and
/// stays if the talk is rejected, as today.</item>
/// <item>Recording: only accepted talk settles. <see cref="BreakCallbacks.SettleStrictAsync"/> runs the word rule on the aired line and stamps the
/// offer used or unused, from the memo alone, with the utterance id the snapshot carries (F220.15). A rejected attempt settles nothing.</item>
/// <item>A fault in the planner, the store or the roster is not swallowed here: the rule book reports it as "Could not shape" / "Could not record" (F220.9).</item>
/// </list>
/// The ask text lives in the legacy user prompt today and rides under "For this break" in the system prompt in rules form: one copy of the text, moved.
/// </summary>
public sealed class CallbacksRule(IBreakCallbacks callbacks, IMemoryBreakGate gate) : IShapingTalkRule, IRecordingTalkRule
{
    public string Id => CallbackOfferMemo.RuleId;

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public TalkRuleDescription Describe() => new(
        "Callbacks.",
        "Now and then asks a lead-in or a back-announce to work in one thing the DJ remembers, in a few words and without saying that they remember it. A DJ gets at most the number of callbacks an hour the setting allows, never two in ten minutes, and the memory must have a word of its own that is not a name on the station. A callback counts as used when the line that aired says one of that memory's words. Only talk that aired counts: talk that was thrown away uses nothing up. Switched off, no memory is offered.",
        "A DJ who remembers the listener's life now and then sounds like someone who has been here all along.",
        "The DJ has a dog named Daisy. The lead-in says \"Daisy would have howled at this one\", so the callback is used.");

    public async ValueTask<RuleShaping?> ShapeAsync(TalkSnapshot before, CancellationToken ct)
    {
        if (!Enum.TryParse<SegmentKind>(before.SegmentKind, out var kind) || await gate.OpenAsync(before, ct) is not { } memoryBreak)
            return null;

        var track = before.Track;
        var ask = await callbacks.PlanStrictAsync(
            new CallbackBreak(kind, memoryBreak.Speaker.PersonaId, before.Station.Name, track?.Title, track?.Artist, track?.Album),
            memoryBreak.Persona, memoryBreak.Card, ct);
        return ask is null
            ? null
            : new RuleShaping(
                string.Join('\n', LlmPromptBuilder.BuildCallbackAskLines(ask)),
                JsonSerializer.SerializeToElement(new CallbackOfferMemo(ask), TalkSnapshotJson.Options));
    }

    public async ValueTask AcceptedAsync(TalkSnapshot accepted, CancellationToken ct)
    {
        if (accepted.MemoOf(this) is not { } stored || stored.Deserialize<CallbackOfferMemo>(TalkSnapshotJson.Options) is not { } memo)
            return;

        await callbacks.SettleStrictAsync(memo.Ask, accepted.UtteranceId, RuleText.Of(accepted), fallback: false, ct);
    }
}
