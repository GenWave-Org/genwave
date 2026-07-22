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
sealed class BoothLogWriter(
    ChannelWriter<BoothLogEntryRequest> queue,
    IActivePersonaAccessor personaAccessor,
    ILogger<BoothLogWriter> logger) : IBoothLogEventConsumer
{
    public void Publish(StationEvent evt)
    {
        // Persona stamp captured HERE, synchronously, at air time (SPEC F84.6, STORY-215) — not
        // later by BoothLogDrainService. IActivePersonaAccessor.ActivePersonaId is a pure in-memory
        // read (no store round trip), safe on this hot path. Capturing it now rather than at drain
        // time is the whole point: the queue between here and the drain loop is bounded (512) and can
        // back up under a DB outage — resolving at drain time would mis-stamp an already-queued
        // track-start with whatever persona is active once the backlog clears, not the one that was
        // actually on air when the track started. Only a track-start row is ever a stamp candidate;
        // patter/mode-change rows always publish PersonaId: null.
        var request = evt switch
        {
            // Artist (SPEC F84.1, STORY-215, PLAN T70) rides the same capture-at-publish-time
            // discipline as PersonaId just above — never re-derived later, never surfaced through
            // IBoothLogReader.
            TrackAired t => new BoothLogEntryRequest("track-started", Summarize(t), personaAccessor.ActivePersonaId, t.Artist),
            SegmentGenerated s => new BoothLogEntryRequest("patter-aired", Summarize(s), PersonaId: null),
            DegradationModeChanged d => new BoothLogEntryRequest("mode-changed", Summarize(d), PersonaId: null),
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
