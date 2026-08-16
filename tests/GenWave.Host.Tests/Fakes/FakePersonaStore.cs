using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// In-memory, seedable <see cref="IPersonaStore"/> double — Story333's own T295 write-path Facts use
/// this to arrange a known persona id for <c>PersonaAvatarController</c>'s own object-level existence
/// check (security-api IDOR discipline: the route id must belong to a real persona before any write is
/// attempted) through <c>WebApplicationFactory&lt;Program&gt;</c>, with no live Postgres fixture.
/// Only <see cref="GetByIdAsync"/> is exercised by that controller — every other member this interface
/// requires throws <see cref="NotSupportedException"/>, mirroring Story120_PersonaEndpoints.cs's own
/// file-scoped <c>FakePersonaStore</c> idiom for members a given Fact set never calls.
/// </summary>
sealed class FakePersonaStore : IPersonaStore
{
    readonly Dictionary<long, Persona> byId = new();

    /// <summary>Arranges a persona row this double's <see cref="GetByIdAsync"/> will report as
    /// existing — the only capability Story333's own T295 Facts need from this double.</summary>
    public void Seed(Persona persona) => byId[persona.Id] = persona;

    public Task<Persona?> GetByIdAsync(long id, CancellationToken ct) =>
        Task.FromResult(byId.TryGetValue(id, out var persona) ? persona : null);

    public Task<IReadOnlyList<Persona>> GetAllAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story333's T295 write-path Facts.");

    public Task<PersonaWriteResult> CreateAsync(PersonaDraft draft, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story333's T295 write-path Facts.");

    public Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story333's T295 write-path Facts.");

    public Task<PersonaWriteResult> DeleteAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story333's T295 write-path Facts.");

    public Task<PersonaCard?> GetCardByIdAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story333's T295 write-path Facts.");

    public Task<long?> GetIdBySlugAsync(string slug, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story333's T295 write-path Facts.");
}
