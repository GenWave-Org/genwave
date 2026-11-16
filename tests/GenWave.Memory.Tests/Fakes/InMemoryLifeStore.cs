using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Memory.Tests.Fakes;

/// <summary>
/// In-memory, simplified stand-in for the life half of persona_memory + memory_event. RecentLifeAsync and
/// CountExtractedSinceAsync model only what the Memory specs need (every row is extracted; a seeded row may be forgotten,
/// which hides it from RecentLifeAsync and shows it to ForgottenLifeAsync). The authored/forgotten counting, dedupe and cap
/// rules are the SQL in PersonaMemoryRepository, pinned by GenWave.MediaLibrary.Tests Story537_LifeRetentionSql.
/// </summary>
public sealed class InMemoryLifeStore(TimeProvider? time = null) : ILifeStore
{
    readonly TimeProvider clock = time ?? TimeProvider.System;

    public sealed class Row
    {
        public long Id { get; init; }
        public long PersonaId { get; init; }
        public required string Content { get; init; }
        public long? EventId { get; init; }
        public bool Aired { get; set; }
        public bool Forgotten { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
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
    public Row Seed(long personaId, string content, bool aired = false, TimeSpan? age = null, bool forgotten = false)
    {
        var row = new Row
        {
            Id = ++lastId, PersonaId = personaId, Content = content, Aired = aired, Forgotten = forgotten,
            CreatedAt = clock.GetUtcNow() - (age ?? TimeSpan.FromDays(1)),
        };
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

    public Task<IReadOnlyList<string>> RecentLifeAsync(long personaId, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(
            rows.Where(r => r.PersonaId == personaId && !r.Forgotten).OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
                .Take(limit).Select(r => r.Content).ToList());

    public Task<IReadOnlyList<string>> ForgottenLifeAsync(long personaId, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(
            rows.Where(r => r.PersonaId == personaId && r.Forgotten).OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
                .Take(limit).Select(r => r.Content).ToList());

    public Task<int> CountExtractedSinceAsync(long personaId, DateTimeOffset since, CancellationToken ct) =>
        Task.FromResult(rows.Count(r => r.PersonaId == personaId && r.CreatedAt >= since));

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
            var row = new Row { Id = ++lastId, PersonaId = c.PersonaId, Content = c.Content, EventId = eventId, CreatedAt = clock.GetUtcNow() };
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
