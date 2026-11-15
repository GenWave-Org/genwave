using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Memory.Tests.Fakes;

/// <summary>
/// In-memory, simplified stand-in for the life half of persona_memory + memory_event (it has no forgotten rows). Its
/// dedupe and cap are an approximation this project's specs lean on; the real rule is the SQL in PersonaMemoryRepository,
/// pinned by GenWave.MediaLibrary.Tests Story537_LifeRetentionSql.
/// </summary>
public sealed class InMemoryLifeStore : ILifeStore
{
    public sealed class Row
    {
        public long Id { get; init; }
        public long PersonaId { get; init; }
        public required string Content { get; init; }
        public long? EventId { get; init; }
        public bool Aired { get; set; }
        public string Kind => MemoryKinds.Life;
        public string Source => "extracted";
    }

    public sealed record Happening(long Id, string Kind, string Summary);

    readonly List<Row> rows = [];
    long lastId;
    readonly List<Happening> happenings = [];

    public IReadOnlyList<Row> Rows => rows;

    public IReadOnlyList<Happening> Happenings => happenings;

    /// <summary>Plants a row as if it had been stored earlier.</summary>
    public Row Seed(long personaId, string content, bool aired = false)
    {
        var row = new Row { Id = ++lastId, PersonaId = personaId, Content = content, Aired = aired };
        rows.Add(row);
        return row;
    }

    public async Task<bool> AddAsync(NewLifeClaim claim, CancellationToken ct)
    {
        var before = rows.Count;
        await WriteAsync(null, [claim], ct);
        return rows.Count > before;
    }

    public Task AddOutingAsync(NewMemoryEvent outing, IReadOnlyList<NewLifeClaim> claims, CancellationToken ct) =>
        WriteAsync(outing, claims, ct);

    Task WriteAsync(NewMemoryEvent? outing, IReadOnlyList<NewLifeClaim> claims, CancellationToken ct)
    {
        var fresh = new List<NewLifeClaim>();
        foreach (var claim in claims)
        {
            var wanted = LifeMemoryRules.Normalise(claim.Content);
            if (!rows.Concat(fresh.Select(f => new Row { PersonaId = f.PersonaId, Content = f.Content }))
                    .Any(r => r.PersonaId == claim.PersonaId && LifeMemoryRules.Normalise(r.Content) == wanted))
                fresh.Add(claim);
        }

        if (fresh.Count == 0)
            return Task.CompletedTask;

        long? eventId = null;
        if (outing is not null)
        {
            eventId = happenings.Count + 1;
            happenings.Add(new Happening(eventId.Value, outing.Kind, outing.Summary));
        }

        var written = fresh.Select(c =>
        {
            var row = new Row { Id = ++lastId, PersonaId = c.PersonaId, Content = c.Content, EventId = eventId };
            rows.Add(row);
            return row;
        }).ToList();

        foreach (var personaId in fresh.Select(c => c.PersonaId).Distinct())
        {
            var excess = rows.Count(r => r.PersonaId == personaId) - LifeMemoryRules.Cap;
            var evict = rows.Where(r => r.PersonaId == personaId && !written.Contains(r))
                .OrderBy(r => r.Aired).ThenBy(r => r.Id).Take(Math.Max(0, excess)).ToList();
            foreach (var row in evict)
                rows.Remove(row);
        }

        return Task.CompletedTask;
    }
}
