using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Tts;

namespace GenWave.TalkRules;

/// <summary>
/// The one "memory is on for this break" test the two memory shapers (<c>callbacks</c>, <c>never-mention</c>) share, as
/// <c>LlmCopyWriter.MemoryOnAir</c> asks it today (F218.21): on air, an LLM-authored (not announcement) DJ break, a speaker, a
/// context window at the 8192 floor, and a persona section. The persona-section test is <see cref="LlmPromptBuilder.HasPersonaSection(GenWave.Core.Domain.Persona?, GenWave.Core.Domain.PersonaCard?, GenWave.Core.Domain.QuirkSelection?)"/>
/// on the persona and card alone: the rules form never reads the quirk pick, which belongs to <c>catchphrase-limits</c>.
/// Each rule opens the gate separately, so a memory break reads the persona and the card twice where legacy reads them once: the accepted cost of rule independence.
/// A service, not a rule: it holds no state and no rule reads another through it.
/// </summary>
public sealed class MemoryBreakGate(IPersonaStore personas, IPersonaCardByIdSource cards, ILlmContextWindow window) : IMemoryBreakGate
{
    /// <summary>The break to offer memory for, or null when memory plays no part in it.</summary>
    public async Task<MemoryBreak?> OpenAsync(TalkSnapshot before, CancellationToken ct)
    {
        if (before.Kind != TalkKinds.DjLine || before.Preview || before.SegmentKind == nameof(SegmentKind.Announcement)
            || before.Speakers.FirstOrDefault() is not { } speaker || !window.Budget.MemoryOn)
            return null;

        var persona = await personas.GetByIdAsync(speaker.PersonaId, ct);
        var card = await cards.GetCardAsync(speaker.PersonaId, ct);
        return LlmPromptBuilder.HasPersonaSection(persona, card) ? new MemoryBreak(speaker, persona, card, window.Budget) : null;
    }
}
