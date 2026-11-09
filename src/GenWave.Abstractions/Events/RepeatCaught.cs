namespace GenWave.Core.Events;

/// <summary>
/// The DJ's freshly written copy repeated a recent line or a spent quirk (SPEC F218.13, STORY-534,
/// PLAN T693). <paramref name="PersonaId"/> is the persona that wrote it; <paramref name="Phrase"/> names
/// the repeated phrase(s), joined by <c>", "</c> when there are several. Published once per caught
/// attempt, so a re-ask that repeats again publishes a second event before the template airs.
/// </summary>
public sealed record RepeatCaught(long PersonaId, string Phrase) : StationEvent;
