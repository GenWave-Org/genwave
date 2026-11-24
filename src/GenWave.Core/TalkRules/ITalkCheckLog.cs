namespace GenWave.Core.TalkRules;

/// <summary>Bounded queue with a background writer; never delays air.</summary>
public interface ITalkCheckLog
{
    /// <summary>Queues the row without waiting; false when the queue is full and the row was dropped.</summary>
    bool TryRecord(TalkCheckRecord record);
}
