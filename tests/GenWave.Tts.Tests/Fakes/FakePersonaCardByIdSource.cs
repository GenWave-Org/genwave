namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>Minimal <see cref="IPersonaCardByIdSource"/> double (STORY-456, PLAN T525): a plain
/// dictionary lookup — a missing id resolves to <see langword="null"/>, mirroring the seam's own
/// "no such persona or no card" contract, with no store/connection behind it at all.</summary>
public sealed class FakePersonaCardByIdSource : IPersonaCardByIdSource
{
    readonly Dictionary<long, PersonaCard> cards = [];

    public void Add(long personaId, PersonaCard card) => cards[personaId] = card;

    public Task<PersonaCard?> GetCardAsync(long personaId, CancellationToken ct) =>
        Task.FromResult(cards.GetValueOrDefault(personaId));
}
