using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F189.2, gh-#772, PLAN T525) — resolves the one <see cref="SpeakerSnapshot"/> a TTS
/// render carries on its request, instead of re-reading the ambient ActivePersona*Cache trio at
/// render time. Never throws: a card that cannot be resolved (unknown id, or a store fault)
/// degrades to the station snapshot with a WARN, never an exception the caller has to handle.
/// </summary>
public interface ISpeakerSnapshotSource
{
    /// <summary>The station's own snapshot — no persona on air, or the caller wants the station
    /// voice regardless of who is on air.</summary>
    Task<SpeakerSnapshot> ForStationAsync(StationIdentity identity, CancellationToken ct);

    /// <summary>The snapshot for the persona identified by <paramref name="personaId"/>. Degrades
    /// to <see cref="ForStationAsync"/> (with a WARN naming the id) when the persona has no card,
    /// or when resolving the card faults.</summary>
    Task<SpeakerSnapshot> ForPersonaAsync(long personaId, CancellationToken ct);
}
