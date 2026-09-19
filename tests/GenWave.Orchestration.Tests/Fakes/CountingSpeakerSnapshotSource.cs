using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Counting <see cref="ISpeakerSnapshotSource"/> double (PLAN T527, SPEC F188.4, AC9) — records how
/// many times each method is actually called, so a spec can assert the per-plan memo
/// (<see cref="SpeakerResolution"/>) resolves the station once and each distinct persona once,
/// however many slots in the same plan name them. A station snapshot always answers the same fixed
/// shape (<see cref="StationSnapshot"/>); a persona snapshot defaults to a fixed per-id shape unless
/// <see cref="Personas"/> overrides that id.
/// </summary>
sealed class CountingSpeakerSnapshotSource : ISpeakerSnapshotSource
{
    public int StationCalls { get; private set; }
    public Dictionary<long, int> PersonaCalls { get; } = [];
    public Dictionary<long, SpeakerSnapshot> Personas { get; } = [];

    public Task<SpeakerSnapshot> ForStationAsync(StationIdentity identity, CancellationToken ct)
    {
        StationCalls++;
        return Task.FromResult(StationSnapshot(identity));
    }

    public Task<SpeakerSnapshot> ForPersonaAsync(long personaId, CancellationToken ct)
    {
        PersonaCalls[personaId] = PersonaCalls.GetValueOrDefault(personaId) + 1;

        var snapshot = Personas.TryGetValue(personaId, out var overridden) ? overridden : DefaultPersonaSnapshot(personaId);
        return Task.FromResult(snapshot);
    }

    static SpeakerSnapshot StationSnapshot(StationIdentity identity) =>
        new(null, null, identity.Voice, 1.0, [], [], "station");

    static SpeakerSnapshot DefaultPersonaSnapshot(long personaId) =>
        new(personaId, $"persona-{personaId}", $"voice-{personaId}", 1.0 + personaId / 10.0, [], [], $"persona-{personaId}");
}
