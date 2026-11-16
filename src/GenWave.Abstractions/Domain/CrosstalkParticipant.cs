namespace GenWave.Core.Domain;

/// <summary>
/// One persona taking part in an aired crosstalk exchange (SPEC F218.15): the id a memory row is written
/// under, and the name the other participant's memory calls them by. Names either side of any on-air persona
/// pair: a crosstalk exchange or a sign-off/sign-on handoff (<see cref="HandoffParticipants"/>).
/// </summary>
/// <param name="PersonaId">The persona's id.</param>
/// <param name="Name">The persona's display name at the time the exchange was written.</param>
public sealed record CrosstalkParticipant(long PersonaId, string Name);
