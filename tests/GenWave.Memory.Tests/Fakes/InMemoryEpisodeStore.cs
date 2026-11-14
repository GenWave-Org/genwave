using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Memory.Tests.Fakes;

/// <summary>
/// In-memory stand-in for the episode half of persona_memory + memory_event. Its retention
/// (28 days, cap 50) is a rough copy that no spec checks; the real rule is the SQL in
/// PersonaMemoryRepository, pinned by GenWave.MediaLibrary.Tests Story536_EpisodeRetentionSql.
/// </summary>
public sealed class InMemoryEpisodeStore(FakeTimeProvider clock) : IEpisodeStore
{
    const int CapPerKind = 50;
    static readonly TimeSpan Window = TimeSpan.FromDays(28);

    public sealed record Row(long Id, long PersonaId, string Content, DateTimeOffset OccurredAt, long? EventId)
    {
        public string Kind => MemoryKinds.Episode;

        public string Source => "accrued";
    }

    public sealed record Happening(long Id, string Kind, string Summary, DateTimeOffset OccurredAt);

    readonly List<Row> rows = [];
    readonly List<Happening> happenings = [];

    public IReadOnlyList<Row> Rows => rows;

    public IReadOnlyList<Happening> Happenings => happenings;

    public Task AppendAsync(NewEpisode episode, CancellationToken ct)
    {
        Insert(episode, null);
        Evict(episode.PersonaId);
        return Task.CompletedTask;
    }

    public Task AppendSharedAsync(NewMemoryEvent happening, IReadOnlyList<NewEpisode> episodes, CancellationToken ct)
    {
        var id = happenings.Count + 1;
        happenings.Add(new Happening(id, happening.Kind, happening.Summary, happening.OccurredAt));
        foreach (var episode in episodes)
            Insert(episode, id);
        foreach (var personaId in episodes.Select(e => e.PersonaId).Distinct())
            Evict(personaId);
        return Task.CompletedTask;
    }

    /// <summary>Plants an episode without eviction, the way a direct SQL insert plants one a test needs already aged.</summary>
    public void Seed(long personaId, string content, TimeSpan age) =>
        rows.Add(new Row(rows.Count + 1, personaId, content, clock.GetUtcNow() - age, null));

    void Insert(NewEpisode episode, long? eventId) =>
        rows.Add(new Row(rows.Count + 1, episode.PersonaId, episode.Content, episode.OccurredAt, eventId));

    void Evict(long personaId)
    {
        var mine = rows.Where(r => r.PersonaId == personaId).OrderByDescending(r => r.OccurredAt).ThenByDescending(r => r.Id).ToList();
        var cutoff = clock.GetUtcNow() - Window;
        foreach (var row in mine.Skip(CapPerKind).Concat(mine.Where(r => r.OccurredAt < cutoff)).Distinct())
            rows.Remove(row);
    }
}
