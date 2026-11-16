namespace GenWave.Core.Events;

/// <summary>
/// The DJ's freshly written copy repeated a recent line or a spent quirk (SPEC F218.13, STORY-534,
/// PLAN T693). <paramref name="PersonaId"/> is the persona that wrote it; <paramref name="Phrase"/> names
/// the repeated phrase(s), joined by <c>", "</c> when there are several. Published one or more times per
/// caught attempt (crosstalk: up to one Trimmed and one Dropped per speaker), so a re-ask that repeats again
/// publishes further events, before its trimmed remainder (<see cref="RepeatOutcome.Trimmed"/>) or the
/// template airs.
/// <paramref name="Outcome"/> (SPEC F218.38) says what the writer did about it. No current publisher passes
/// <see langword="null"/>; it stays optional only so earlier event producers still compile.
/// </summary>
public sealed record RepeatCaught(long PersonaId, string Phrase, RepeatOutcome? Outcome = null) : StationEvent;
