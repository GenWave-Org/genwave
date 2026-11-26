using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>An <see cref="IPersonaStore"/> that lists a fixed roster and finds nobody by id; nothing else is exercised.</summary>
internal sealed class RosterPersonaStore(params string[] names) : IPersonaStore
{
    public Task<IReadOnlyList<Persona>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Persona>>(
            names.Select((name, i) => new Persona(i + 1, name, "", "", "", DateTime.UtcNow, DateTime.UtcNow)).ToList());

    public Task<Persona?> GetByIdAsync(long id, CancellationToken ct) => Task.FromResult<Persona?>(null);

    public Task<PersonaWriteResult> CreateAsync(PersonaDraft draft, CancellationToken ct) => throw new NotSupportedException();

    public Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct) => throw new NotSupportedException();

    public Task<PersonaWriteResult> DeleteAsync(long id, CancellationToken ct) => throw new NotSupportedException();

    public Task<PersonaCard?> GetCardByIdAsync(long id, CancellationToken ct) => throw new NotSupportedException();

    public Task<long?> GetIdBySlugAsync(string slug, CancellationToken ct) => throw new NotSupportedException();
}
