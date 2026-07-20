using System.Threading.Channels;
using GenWave.Core.Abstractions;
using GenWave.Core.Events;
using Microsoft.Extensions.Logging;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The booth log's <see cref="IStationEventSink"/> consumer (SPEC F72.1, STORY-195): translates the
/// three named narrative kinds — track starts (<see cref="TrackAired"/>), patter airs
/// (<see cref="SegmentGenerated"/>), and degradation mode changes (<see cref="DegradationModeChanged"/>,
/// T32/STORY-188) — into an operator-readable (kind, summary) pair and enqueues it for
/// <see cref="BoothLogDrainService"/> to persist. Every other event type (library mutations, settings
/// writes, enrichment completion, …) is ignored — it carries no booth-log narrative.
///
/// <see cref="Publish"/> sits on the same hot paths <see cref="IStationEventSink"/>'s own contract
/// warns about (the feeder tick, a TTS render) — it never touches Postgres itself. The channel write
/// is a non-blocking <see cref="ChannelWriter{T}.TryWrite"/>, so a full queue (a sustained DB
/// outage/backlog) drops the newest entry with a WARN rather than ever stalling playout — the sink
/// contract's "must return promptly" holds unconditionally.
/// </summary>
sealed class BoothLogWriter(ChannelWriter<BoothLogEntryRequest> queue, ILogger<BoothLogWriter> logger)
    : IBoothLogEventConsumer
{
    public void Publish(StationEvent evt)
    {
        var request = evt switch
        {
            TrackAired t => new BoothLogEntryRequest("track-started", Summarize(t)),
            SegmentGenerated s => new BoothLogEntryRequest("patter-aired", Summarize(s)),
            DegradationModeChanged d => new BoothLogEntryRequest("mode-changed", Summarize(d)),
            _ => null,
        };
        if (request is null) return;

        if (!queue.TryWrite(request))
            logger.LogWarning("Booth log queue full — dropping {Kind} entry", request.Kind);
    }

    static string Summarize(TrackAired t) => (t.Title, t.Artist) switch
    {
        ({ } title, { } artist) => $"Started '{title}' by {artist}",
        ({ } title, null) => $"Started '{title}'",
        _ => $"Started track {t.MediaId}",
    };

    static string Summarize(SegmentGenerated s) => string.IsNullOrWhiteSpace(s.Voice)
        ? $"Patter aired ({s.Kind})"
        : $"Patter aired ({s.Kind}, voice: {s.Voice})";

    static string Summarize(DegradationModeChanged d) =>
        $"LLM degradation: {d.Previous} → {d.New} ({d.Cause})";
}
