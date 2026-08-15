namespace GenWave.Tts;

using GenWave.Core.Domain;

/// <summary>
/// Everything <see cref="CrosstalkAssembler"/> needs to render and mix one exchange (SPEC F127.5,
/// F127.6, STORY-327) — <see cref="Script"/> already validated by <see cref="CrosstalkScriptWriter"/>,
/// <see cref="HostCard"/>/<see cref="NeighborCard"/> already CAST by the caller (a LATER task's
/// <c>CrosstalkPlanner</c>, SPEC F127.2), mirroring <see cref="CrosstalkExchangeRequest"/>'s own
/// "never resolves who is on either side of the booth" posture one seam over. Which card renders
/// which line is decided per <see cref="CrosstalkLine.Speaker"/>, not here.
/// </summary>
public sealed record CrosstalkAssemblyRequest(CrosstalkScript Script, PersonaCard HostCard, PersonaCard NeighborCard);
