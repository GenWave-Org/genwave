namespace GenWave.Orchestration;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// Per-plan memo over <see cref="ISpeakerSnapshotSource"/> (SPEC F188.4): at most one
/// <c>ForStationAsync</c> call and one <c>ForPersonaAsync</c> call per distinct persona id,
/// however many slots in the same plan name them. Created once per
/// <see cref="BreakPlanner.PlanAsync"/> call and threaded through the slot builders — never
/// shared across plans, since a persona's card (or the station identity) can change between one
/// break and the next.
/// </summary>
sealed class SpeakerResolution(ISpeakerSnapshotSource source)
{
    SpeakerSnapshot? station;
    readonly Dictionary<long, SpeakerSnapshot> personas = [];

    public async Task<SpeakerSnapshot> StationAsync(StationIdentity identity, CancellationToken ct)
    {
        if (station is null) station = await source.ForStationAsync(identity, ct);
        return station;
    }

    public async Task<SpeakerSnapshot> PersonaAsync(long personaId, CancellationToken ct)
    {
        if (personas.TryGetValue(personaId, out var cached)) return cached;

        var resolved = await source.ForPersonaAsync(personaId, ct);
        personas[personaId] = resolved;
        return resolved;
    }
}
