using GenWave.Core.TalkRules;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>An in-memory check store that pages like the real one (id order, keyset) and remembers what the replay asked of it.</summary>
internal sealed class PagedCheckStore(IReadOnlyList<TalkCheckEntry> entries) : ITalkCheckStore
{
    public int LargestLimitAsked { get; private set; }

    public int Appends { get; private set; }

    public int UnboundedReads { get; private set; }

    public Task AppendAsync(TalkCheckRecord record, CancellationToken ct)
    {
        Appends++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TalkCheckEntry>> ReadSinceAsync(DateTimeOffset since, CancellationToken ct)
    {
        UnboundedReads++;
        return Task.FromResult(entries);
    }

    public Task<IReadOnlyList<TalkCheckEntry>> ReadPageAsync(DateTimeOffset since, long afterId, int limit, CancellationToken ct)
    {
        LargestLimitAsked = Math.Max(LargestLimitAsked, limit);
        IReadOnlyList<TalkCheckEntry> page = entries
            .Where(entry => entry.Id > afterId && entry.CheckedAt >= since)
            .OrderBy(entry => entry.Id)
            .Take(Math.Min(limit, ITalkCheckStore.MaxPageSize))
            .ToList();
        return Task.FromResult(page);
    }
}
