using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Tts;

namespace GenWave.TalkRules;

/// <summary>
/// Show flavour. Shaping only: on an on-air lead-in or back-announce (<see cref="LlmPromptBuilder.IsPatterFactKind"/>) whose one extra line is
/// still free (<see cref="TalkSnapshot.ExtraLineTaken"/> false: no context fact, no crosstalk), takes the show's due line from
/// <see cref="IShowFlavorLineSource"/> (<c>ShowFlavorLineGate</c>) once, as <c>LlmCopyWriter.TakeDueShowFlavorLineForOnAirRender</c> does today.
/// The note is <see cref="LlmPromptBuilder.BuildShowFlavorPatterLine"/>, the line the legacy user prompt carries too. The take consumes the show's
/// cadence window, so nothing is asked of the source unless the line can ride. It marks nothing and keeps no state. A fault in the source is not
/// swallowed here: the rule book reports it as "Could not shape" (F220.9).
/// </summary>
public sealed class ShowFlavourRule(IShowFlavorLineSource source) : IShapingTalkRule
{
    public string Id => "show-flavour";

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public TalkRuleDescription Describe() => new(
        "Show flavour.",
        "Now and then, as often as the show patter setting allows, lets a lead-in or a back-announce colour its delivery with the flavour text of the show that is on the air. It never rides on a break that already carries a context fact or a crosstalk, and a preview never asks.",
        "A show that sounds like itself gives the schedule a character.",
        "The Night Shift show is on, its flavour is due, and the lead-in is told the show is about cold coffee.");

    public ValueTask<RuleShaping?> ShapeAsync(TalkSnapshot before, CancellationToken ct)
    {
        if (before.Kind != TalkKinds.DjLine || before.Preview || before.ExtraLineTaken
            || !Enum.TryParse<SegmentKind>(before.SegmentKind, out var kind) || !LlmPromptBuilder.IsPatterFactKind(kind))
            return ValueTask.FromResult<RuleShaping?>(null);

        var line = LlmPromptBuilder.BuildShowFlavorPatterLine(source.TryTakeDueShowLine());
        return ValueTask.FromResult<RuleShaping?>(line is null ? null : new RuleShaping(line, null));
    }
}
