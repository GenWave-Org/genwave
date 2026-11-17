using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Memory.Tests.Fakes;

/// <summary>
/// In-memory stand-in for the persona_memory table, mirroring PersonaMemoryRepository's never-mention SQL
/// (persona scope, forgotten window, ordering and tiebreaks) on a fake clock.
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

    public Task<IReadOnlyList<string>> RecentlyForgottenAsync(long personaId, TimeSpan within, int take, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<string> result = rows.Where(r => r.PersonaId == personaId && r.ForgottenAt is { } f && f >= now - within)
            .OrderByDescending(r => r.ForgottenAt).ThenByDescending(r => r.Id).Take(take).Select(r => r.Content).ToList();
        return Task.FromResult(result);
    }
}
