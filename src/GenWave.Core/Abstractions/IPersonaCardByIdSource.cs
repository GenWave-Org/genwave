using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F189.2, gh-#772, PLAN T525) — the narrow ISP read a persona's card by id, nothing
/// else off <see cref="IPersonaStore"/>'s wider CRUD surface. <see cref="GenWave.Tts"/>'s
/// SpeakerSnapshotSource is the one consumer that needs this and nothing more; GenWave.Host's
/// implementation is a thin adapter over the SAME <see cref="IPersonaStore"/> singleton the rest
/// of the Host already registers, never a second store or a second query.
/// </summary>
public interface IPersonaCardByIdSource
{
    /// <summary>
    /// Returns the card for <paramref name="personaId"/>, or null when no such persona exists or
    /// the row carries no card yet (same "a real card or nothing" posture as
    /// <see cref="IPersonaStore.GetCardByIdAsync"/>, which this seam delegates to).
    /// </summary>
    Task<PersonaCard?> GetCardAsync(long personaId, CancellationToken ct);
}
