using GenWave.Core.Abstractions;
using GenWave.Core.TalkRules;
using GenWave.Tts;

namespace GenWave.TalkRules;

/// <summary>
/// Says their own name sometimes. Shaping only: one roll per break, made here (<see cref="ISelfNameRoll"/>: <see cref="Random.Shared"/> at
/// <see cref="LlmPromptBuilder.SelfNameMentionProbability"/>, as <c>LlmCopyWriter</c> rolls today). On a hit, a DJ with a persona section is asked
/// to work their on-air name in: the note is <see cref="LlmPromptBuilder.BuildSelfNameMentionLine"/>, the line the legacy persona section carries too.
/// The persona gate is <see cref="LlmPromptBuilder.HasPersonaSection(GenWave.Core.Domain.Persona?, GenWave.Core.Domain.PersonaCard?, GenWave.Core.Domain.QuirkSelection?)"/>
/// on the persona and card alone (the quirk pick is <c>catchphrase-limits</c>'s). The speaker is checked first, then the roll is taken for every DJ break with one, as legacy
/// takes it; a switched-off rule never reaches it. It marks nothing and keeps no state. A fault in a store is not swallowed here: the rule book reports it as "Could not shape" (F220.9).
/// </summary>
public sealed class SelfNameRule(ISelfNameRoll roll, IPersonaStore personas, IPersonaCardByIdSource cards) : IShapingTalkRule
{
    public string Id => "self-name";

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public TalkRuleDescription Describe() => new(
        "Says their own name sometimes",
        $"On about {LlmPromptBuilder.SelfNameMentionProbability:P0} of breaks, asks the DJ to work their own on-air name in once, where it lands naturally. A DJ with no personality text of their own is never asked.",
        "Real radio DJs now and then say who they are, and a name said every break would turn into a tic.",
        "The roll lands, and the lead-in is asked to say \"you're with Flip\".");

    public async ValueTask<RuleShaping?> ShapeAsync(TalkSnapshot before, CancellationToken ct)
    {
        if (before.Speakers.FirstOrDefault() is not { } speaker || !roll.Rolls())
            return null;

        var persona = await personas.GetByIdAsync(speaker.PersonaId, ct);
        var card = await cards.GetCardAsync(speaker.PersonaId, ct);
        if (!LlmPromptBuilder.HasPersonaSection(persona, card) || LlmPromptBuilder.ResolveName(persona, card) is not { } name)
            return null;

        return new RuleShaping(LlmPromptBuilder.BuildSelfNameMentionLine(name), null);
    }
}
