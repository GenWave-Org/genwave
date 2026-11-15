using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Memory.Tests.Fakes;

/// <summary>A persona roster for the life extractor: only <see cref="GetAllAsync"/> is answered.</summary>
public sealed class FakePersonaRoster(params (long Id, string Name)[] personas) : IPersonaStore
{
    public Task<IReadOnlyList<Persona>> GetAllAsync(CancellationToken ct)
    {
        IReadOnlyList<Persona> roster = personas
            .Select(p => new Persona(p.Id, p.Name, "", "", "", DateTime.UnixEpoch, DateTime.UnixEpoch))
            .ToList();
        return Task.FromResult(roster);
    }

    public Task<Persona?> GetByIdAsync(long id, CancellationToken ct) => throw new NotSupportedException();

    public Task<PersonaWriteResult> CreateAsync(PersonaDraft draft, CancellationToken ct) => throw new NotSupportedException();

    public Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct) => throw new NotSupportedException();

    public Task<PersonaWriteResult> DeleteAsync(long id, CancellationToken ct) => throw new NotSupportedException();

    public Task<PersonaCard?> GetCardByIdAsync(long id, CancellationToken ct) => throw new NotSupportedException();

    public Task<long?> GetIdBySlugAsync(string slug, CancellationToken ct) => throw new NotSupportedException();
}
