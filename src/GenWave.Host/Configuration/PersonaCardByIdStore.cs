using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Configuration;

/// <summary>
/// The Host-side <see cref="IPersonaCardByIdSource"/> implementation (SPEC F189.2, gh-#772, PLAN
/// T525): a thin adapter over the SAME <see cref="IPersonaStore"/> singleton
/// <c>StationSettingsHostingExtensions.AddGenWaveStationSettings</c> already registers — no new
/// SQL, no second store, no connection of its own. Exists only so <c>GenWave.Tts</c>'s
/// SpeakerSnapshotSource can depend on the narrow ISP read seam it actually needs, never
/// <see cref="IPersonaStore"/>'s wider CRUD surface.
/// </summary>
sealed class PersonaCardByIdStore(IPersonaStore personaStore) : IPersonaCardByIdSource
{
    public Task<PersonaCard?> GetCardAsync(long personaId, CancellationToken ct) =>
        personaStore.GetCardByIdAsync(personaId, ct);
}
