using System.Threading.Channels;
using GenWave.Core.TalkRules;
using Microsoft.Extensions.Logging;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The producer half of the check log (SPEC F220.29): a non-blocking <see cref="ChannelWriter{T}.TryWrite"/>
/// onto a bounded queue that <see cref="TalkCheckDrainService"/> empties. A full queue (a sustained database
/// outage) drops the newest row with a warning; the caller, which is the talk path, never waits (STORY-566 AC7).
/// </summary>
sealed class TalkCheckLog(ChannelWriter<TalkCheckRecord> queue, ILogger<TalkCheckLog> logger) : ITalkCheckLog
{
    public bool TryRecord(TalkCheckRecord record)
    {
        if (queue.TryWrite(record))
        {
            return true;
        }

        logger.LogWarning("Talk check queue full — dropping a {Mode} row, the talk is unaffected", record.Mode);
        return false;
    }
}
