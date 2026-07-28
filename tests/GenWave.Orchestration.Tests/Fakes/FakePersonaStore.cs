using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IPersonaStore"/> double (STORY-241/242, PLAN T120) for
/// <see cref="OnAirPersonaAccessor"/> specs — keyed by id via <see cref="Add"/>/<see cref="AddCard"/>.
/// The accessor only ever calls <see cref="GetByIdAsync"/>/<see cref="GetCardByIdAsync"/>; every other
/// member throws, mirroring the "not exercised by these scenarios" convention every other
/// <see cref="IPersonaStore"/> double in this codebase already follows.
/// </summary>
sealed class FakePersonaStore : IPersonaStore
{
    readonly Dictionary<long, Persona> personas = [];
    readonly Dictionary<long, PersonaCard> cards = [];

    /// <summary>Set to make the next <see cref="GetByIdAsync"/> call throw, simulating a store fault.</summary>
    public Exception? ThrowOnGetById { get; set; }

    /// <summary>Set to make the next <see cref="GetCardByIdAsync"/> call throw, simulating a store
    /// fault (PLAN T120 review F3) — exercises <c>OnAirPersonaAccessor.ResolveCardAsync</c>'s own
    /// <c>lastWarnedCardPersonaId</c> degrade path, distinct from <see cref="ThrowOnGetById"/>'s
    /// <c>ResolveAsync</c> path.</summary>
    public Exception? ThrowOnGetCardById { get; set; }

    public List<long> GetByIdCalls { get; } = [];

    public void Add(Persona persona) => personas[persona.Id] = persona;

    public void AddCard(long personaId, PersonaCard card) => cards[personaId] = card;

    public Task<Persona?> GetByIdAsync(long id, CancellationToken ct)
    {
        GetByIdCalls.Add(id);
        if (ThrowOnGetById is { } ex) throw ex;

        return Task.FromResult(personas.TryGetValue(id, out var persona) ? persona : null);
    }

    public Task<PersonaCard?> GetCardByIdAsync(long id, CancellationToken ct)
    {
        if (ThrowOnGetCardById is { } ex) throw ex;

        return Task.FromResult(cards.TryGetValue(id, out var card) ? card : null);
    }

    public Task<IReadOnlyList<Persona>> GetAllAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by OnAirPersonaAccessor specs.");

    public Task<PersonaWriteResult> CreateAsync(PersonaDraft draft, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by OnAirPersonaAccessor specs.");

    public Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by OnAirPersonaAccessor specs.");

    public Task<PersonaWriteResult> DeleteAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by OnAirPersonaAccessor specs.");

    public Task<long?> GetIdBySlugAsync(string slug, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by OnAirPersonaAccessor specs.");
}
