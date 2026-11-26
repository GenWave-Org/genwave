using GenWave.Core.Domain;
using GenWave.Core.TalkRules;

namespace GenWave.TalkRules;

/// <summary>An on-air DJ break where memory is on and the speaker has a persona section: who speaks, their persona and card, and the memory budget.</summary>
public sealed record MemoryBreak(TalkSpeaker Speaker, Persona? Persona, PersonaCard? Card, MemoryBudget Budget);
