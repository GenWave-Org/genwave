using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IPersonaMemoryAdmin"/> double: rows keyed by (persona, row id), every member scoped by
/// both ids, so a spec proves the controller never reaches across personas. It models persona and row scoping only,
/// not the kind allowlist: FeatureMemoryAdminSql pins that the Postgres store never reaches quirk rows.
/// </summary>
sealed class FakePersonaMemoryAdmin : IPersonaMemoryAdmin
{
    readonly List<(long PersonaId, PersonaMemoryAdminRow Row)> rows = [];
    long nextId = 1;

    public long Seed(long personaId, string kind, string source, string content, int airedCount = 0, DateTime? approvedAt = null)
    {
        var id = nextId++;
        rows.Add((personaId, new PersonaMemoryAdminRow(id, kind, source, content, airedCount, null, DateTime.UtcNow, approvedAt, null)));
        return id;
    }

    public PersonaMemoryAdminRow? Find(long personaId, long rowId) =>
        rows.Where(r => r.PersonaId == personaId && r.Row.Id == rowId).Select(r => r.Row).FirstOrDefault();

    public Task<IReadOnlyList<PersonaMemoryAdminRow>> ListForAdminAsync(long personaId, string? kind, CancellationToken ct)
    {
        IReadOnlyList<PersonaMemoryAdminRow> result = rows
            .Where(r => r.PersonaId == personaId && (kind is null || r.Row.Kind == kind))
            .Select(r => r.Row)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<PersonaMemoryAdminRow> AddAuthoredLoreAsync(long personaId, string text, CancellationToken ct) =>
        Task.FromResult(Find(personaId, Seed(personaId, MemoryKinds.Lore, "authored", text)) ?? throw new InvalidOperationException());

    public Task<PersonaMemoryUpdateResult> UpdateAsync(long personaId, long rowId, PersonaMemoryChange change, CancellationToken ct)
    {
        var index = rows.FindIndex(r => r.PersonaId == personaId && r.Row.Id == rowId);
        if (index < 0)
            return Task.FromResult(new PersonaMemoryUpdateResult(PersonaMemoryUpdateStatus.NotFound, null));
        var row = rows[index].Row;
        if (change.Approve && row.Source != MemorySources.Drafted)
            return Task.FromResult(new PersonaMemoryUpdateResult(PersonaMemoryUpdateStatus.NotDrafted, null));

        row = row with
        {
            Content = change.Text ?? row.Content,
            ApprovedAt = change.Approve ? row.ApprovedAt ?? DateTime.UtcNow : row.ApprovedAt,
            ForgottenAt = change.Forgotten switch { true => row.ForgottenAt ?? DateTime.UtcNow, false => null, null => row.ForgottenAt },
        };
        rows[index] = (personaId, row);
        return Task.FromResult(new PersonaMemoryUpdateResult(PersonaMemoryUpdateStatus.Updated, row));
    }

    public Task<bool> DeleteAsync(long personaId, long rowId, CancellationToken ct) =>
        Task.FromResult(rows.RemoveAll(r => r.PersonaId == personaId && r.Row.Id == rowId) > 0);
}
