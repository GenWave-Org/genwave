using System.Threading.Channels;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using Microsoft.Extensions.Logging;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The booth log's <see cref="IStationEventSink"/> consumer (SPEC F72.1, STORY-195): translates the
/// named narrative kinds — track starts (<see cref="TrackAired"/>), patter airs
/// (<see cref="SegmentGenerated"/>), degradation mode changes (<see cref="DegradationModeChanged"/>,
/// T32/STORY-188), listener-request intake (<see cref="RequestReceived"/>/
/// <see cref="RequestEvicted"/>, SPEC F87.8, STORY-224, PLAN T87), the fulfillment rung's own two
/// outcomes (<see cref="RequestExpired"/>/<see cref="RequestFulfilled"/>, SPEC F87.6/F87.8, STORY-227,
/// PLAN T90), and a dropped handoff ceremony piece (<see cref="HandoffPieceDropped"/>, SPEC F92.4,
/// STORY-243, PLAN T124) — into an operator-readable (kind, summary) pair and enqueues it for
/// <see cref="BoothLogDrainService"/> to persist. Every
/// other event type (library mutations, settings writes, enrichment completion, …) is ignored — it
/// carries no booth-log narrative.
///
/// The request rows carry NO wish text (F87.7/F87.8 discipline) — their summaries below are fixed
/// literals, not derived from anything on the event, because <see cref="RequestReceived"/>/
/// <see cref="RequestEvicted"/>/<see cref="RequestExpired"/>/<see cref="RequestFulfilled"/> structurally
/// carry nothing else to derive from.
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
        //
        // The show stamp (SPEC F121.1, STORY-310, PLAN T242) rides the IDENTICAL discipline off the
        // SAME dependency: personaAccessor.ActiveShowId reads off the same cached snapshot source
        // ActivePersonaId already reads (see IActivePersonaAccessor's own remarks), captured here
        // rather than at drain time for the exact same backlog-mis-stamp reason. Not one read at one
        // instant, though: each property getter independently re-resolves against the current wall
        // clock, so a schedule boundary landing between these two reads can split the pair on this
        // one row at most — accepted (see IActivePersonaAccessor.ActiveShowId's own remarks). It is
        // captured HERE — never carried on TrackAired itself — because a show is a schedule-grid fact
        // (who is on air right now), not a pushed-item one: SegmentKind/PersonaPick ride TrackAired
        // because PlayoutFeeder already captured them off the pushed MediaItem at push time; a show
        // has no such per-item origin, only the on-air answer at the moment this event publishes.
        var request = evt switch
        {
            // Artist (SPEC F84.1, STORY-215, PLAN T70) rides the same capture-at-publish-time
            // discipline as PersonaId just above — never re-derived later, never surfaced through
            // IBoothLogReader. Pick (SPEC F86.1, STORY-217, PLAN T73) rides the SAME discipline: t's
            // own PersonaPick was already captured synchronously by PlayoutFeeder at push time, so
            // reading it here — rather than re-deriving anything — is the whole point (one source of
            // truth shared with the copywriter, F83.1). SegmentKind (SPEC F113.1, STORY-304, PLAN
            // T220) rides the SAME discipline: t.SegmentKind is PlayoutFeeder's own forwarded
            // MediaItem.SegmentKind, stringified to its enum token name (or null for music/engine-
            // initiated) — the demo-hour instrument's genuine AIR-time stamp, never patter-aired's
            // render-time one. ShowId rides personaAccessor.ActiveShowId, same capture-at-publish-time,
            // never-re-derived discipline — the ONE chokepoint this switch arm already is covers
            // music and kinded TrackAired alike (PLAN T242's own "verify one chokepoint" note).
            TrackAired t => new BoothLogEntryRequest(
                "track-started", Summarize(t), personaAccessor.ActivePersonaId, t.Artist, BuildPickStamp(t.PersonaPick),
                ParseMediaId(t.MediaId), SegmentKind: t.SegmentKind?.ToString(), ShowId: personaAccessor.ActiveShowId),
            SegmentGenerated s => new BoothLogEntryRequest("patter-aired", Summarize(s), PersonaId: null),
            DegradationModeChanged d => new BoothLogEntryRequest("mode-changed", Summarize(d), PersonaId: null),
            HandoffPieceDropped h => new BoothLogEntryRequest("handoff-dropped", Summarize(h), PersonaId: null),
            RequestReceived => new BoothLogEntryRequest("request-received", "Request received", PersonaId: null),
            RequestEvicted => new BoothLogEntryRequest("request-evicted", "Request evicted (pending cap)", PersonaId: null),
            RequestExpired => new BoothLogEntryRequest("request-expired", "Request expired", PersonaId: null),
            RequestFulfilled => new BoothLogEntryRequest("request-fulfilled", "Request fulfilled", PersonaId: null),
            _ => null,
        };
        if (request is null) return;

        if (!queue.TryWrite(request))
            logger.LogWarning("Booth log queue full — dropping {Kind} entry", request.Kind);
    }

    /// <summary>
    /// SPEC F86.1 — <see langword="null"/> for every engine-initiated advance and every persona-off
    /// pick (<paramref name="diagnostics"/> itself is null in both cases); otherwise the F86.1
    /// jsonb text, narrowed to firedRules/isExploration only (scores, pool size, and the degradation
    /// step are deliberately never persisted — see <see cref="BoothLogPickStamp"/>'s own remarks).
    /// </summary>
    static string? BuildPickStamp(PersonaPickDiagnostics? diagnostics) =>
        diagnostics is null ? null : BoothLogPickStampSerializer.Serialize(BoothLogPickStamp.FromDiagnostics(diagnostics));

    /// <summary>
    /// gh-#99 — the aired row's numeric catalog id, or <see langword="null"/> for a non-catalog id
    /// (e.g. <c>tts:*</c>). Same capture-at-publish-time discipline as every stamp above.
    /// </summary>
    static long? ParseMediaId(string mediaId) =>
        long.TryParse(mediaId, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

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

    static string Summarize(HandoffPieceDropped h) => $"Handoff {h.Kind} dropped ({h.Cause})";
}
