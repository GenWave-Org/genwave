using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Memory.Tests.Fakes;

/// <summary>
/// In-memory stand-in for the persona_memory table, mirroring PersonaMemoryRepository's recall SQL
/// (persona scope, eligibility, aired gate, ordering and tiebreaks) on a fake clock.
/// </summary>
public sealed class InMemoryPersonaMemory(FakeTimeProvider clock) : IMemoryRecallStore
{
    public sealed class Row
    {
        public long Id { get; init; }
        public long PersonaId { get; init; }
        public required string Kind { get; init; }
        public required string Content { get; init; }
        public string Source { get; init; } = "authored";
        public DateTime CreatedAt { get; init; }
        public DateTime? OccurredAt { get; init; }
        public DateTime? ApprovedAt { get; init; }
        public DateTime? ForgottenAt { get; init; }
        public DateTime? LastAiredAt { get; set; }
        public int AiredCount { get; set; }
    }

    readonly List<Row> rows = [];

    public IReadOnlyList<Row> Rows => rows;

    public Row Add(string kind, string content, string source = "authored", TimeSpan? occurredAgo = null,
        TimeSpan? airedAgo = null, TimeSpan? forgottenAgo = null, bool approved = false, long personaId = 1)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var row = new Row
        {
            Id = rows.Count + 1,
            PersonaId = personaId,
            Kind = kind,
            Content = content,
            Source = source,
            CreatedAt = now,
            OccurredAt = occurredAgo is { } o ? now - o : null,
            ApprovedAt = approved ? now : null,
            ForgottenAt = forgottenAgo is { } f ? now - f : null,
            LastAiredAt = airedAgo is { } a ? now - a : null,
        };
        rows.Add(row);
        return row;
    }

    public Task<IReadOnlyList<RecalledMemory>> CandidatesAsync(long personaId, MemoryCandidateQuery query, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var eligible = rows.Where(r => r.PersonaId == personaId
            && r.Kind == query.Kind
            && r.ForgottenAt is null
            && (r.Source != "drafted" || r.ApprovedAt is not null)
            && (r.Kind != MemoryKinds.Lore || r.Source is "authored" or "drafted")
            && (r.LastAiredAt is null || r.LastAiredAt <= now - query.NotAiredWithin));
        var ordered = query.Order switch
        {
            MemoryRecallOrder.NewestFirst => eligible.OrderByDescending(r => r.OccurredAt ?? r.CreatedAt).ThenByDescending(r => r.Id),
            MemoryRecallOrder.LeastRecentlyAired => eligible.OrderBy(r => r.LastAiredAt is null ? 0 : 1).ThenBy(r => r.LastAiredAt).ThenBy(r => r.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(query), query.Order, "Unknown recall order."),
        };
        IReadOnlyList<RecalledMemory> result = ordered.Take(query.Take)
            .Select(r => new RecalledMemory(r.Id, r.Kind, r.Content, r.OccurredAt)).ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<string>> RecentlyForgottenAsync(long personaId, TimeSpan within, int take, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<string> result = rows.Where(r => r.PersonaId == personaId && r.ForgottenAt is { } f && f >= now - within)
            .OrderByDescending(r => r.ForgottenAt).ThenByDescending(r => r.Id).Take(take).Select(r => r.Content).ToList();
        return Task.FromResult(result);
    }

    public Task MarkAiredAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        foreach (var row in rows.Where(r => ids.Contains(r.Id)))
        {
            row.AiredCount++;
            row.LastAiredAt = clock.GetUtcNow().UtcDateTime;
        }

        return Task.CompletedTask;
    }
}
