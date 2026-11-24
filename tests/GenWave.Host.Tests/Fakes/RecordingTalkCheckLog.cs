using GenWave.Core.TalkRules;

namespace GenWave.Host.Tests.Fakes;

/// <summary>An <see cref="ITalkCheckLog"/> that keeps every row it is handed, so a spec reads what the writer queued.</summary>
sealed class RecordingTalkCheckLog : ITalkCheckLog
{
    readonly List<TalkCheckRecord> records = [];

    public IReadOnlyList<TalkCheckRecord> Records { get { lock (records) return [.. records]; } }

    public bool TryRecord(TalkCheckRecord record)
    {
        lock (records) records.Add(record);
        return true;
    }
}
