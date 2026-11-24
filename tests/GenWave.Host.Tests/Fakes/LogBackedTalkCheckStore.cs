using GenWave.Core.TalkRules;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// A check store over what a <see cref="RecordingTalkCheckLog"/> caught, plus any rows a spec adds by hand (an older row, a
/// row an earlier rule set judged). Row ids follow position, starting at 1. Every row is stamped <see cref="WrittenAt"/> and the
/// window is not applied: the spec controls which rows exist. <see cref="Appends"/> counts writes through the store, which a replay must never make.
/// </summary>
sealed class LogBackedTalkCheckStore(RecordingTalkCheckLog log) : ITalkCheckStore
{
    static readonly DateTimeOffset WrittenAt = new(2026, 11, 9, 20, 0, 0, TimeSpan.FromHours(-6));

    readonly List<TalkCheckRecord> added = [];

    public int Appends { get; private set; }

    /// <summary>Every row the store holds: those the log caught, then those added by hand.</summary>
    public int RowCount => All().Count;

    public void Add(TalkCheckRecord record)
    {
        lock (added) added.Add(record);
    }

    public Task AppendAsync(TalkCheckRecord record, CancellationToken ct)
    {
        Appends++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TalkCheckEntry>> ReadSinceAsync(DateTimeOffset since, CancellationToken ct) =>
        ReadPageAsync(since, 0, int.MaxValue, ct);

    public Task<IReadOnlyList<TalkCheckEntry>> ReadPageAsync(DateTimeOffset since, long afterId, int limit, CancellationToken ct)
    {
        IReadOnlyList<TalkCheckEntry> page = All()
            .Select((record, index) => new TalkCheckEntry(index + 1, WrittenAt, record))
            .Where(entry => entry.Id > afterId)
            .Take(limit)
            .ToList();
        return Task.FromResult(page);
    }

    List<TalkCheckRecord> All()
    {
        lock (added) return [.. log.Records, .. added];
    }
}
