namespace GenWave.Core.Events;

/// <summary>
/// The DJ's freshly written copy repeated a recent line or a spent quirk (SPEC F218.13, STORY-534,
/// PLAN T693). <paramref name="PersonaId"/> is the persona that wrote it; <paramref name="Phrase"/> names
/// the repeated phrase(s), joined by <c>", "</c> when there are several. Published once per caught
/// attempt, so a re-ask that repeats again publishes a second event, before either its trimmed remainder
/// (<see cref="RepeatOutcome.Trimmed"/>) or the template airs.
/// <paramref name="Outcome"/> (SPEC F218.38) says what the writer did about it; <see langword="null"/> for a
/// publisher that has no such choice (crosstalk).
/// </summary>
public sealed record RepeatCaught(long PersonaId, string Phrase, RepeatOutcome? Outcome = null) : StationEvent;
