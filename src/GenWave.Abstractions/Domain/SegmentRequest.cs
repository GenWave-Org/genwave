namespace GenWave.Core.Domain;

/// <summary>
/// Carries every value a TTS template renderer needs to produce one segment.
/// <see cref="Track"/> is null for <see cref="SegmentKind.StationId"/> and
/// <see cref="SegmentKind.TimeDate"/> segments.
/// </summary>
/// <param name="Kind">The role this segment plays in the broadcast flow.</param>
/// <param name="Voice">TTS voice identifier passed through to the synthesizer.</param>
/// <param name="StationName">Human-readable station name for use in templates.</param>
/// <param name="Track">
/// Upcoming track for <see cref="SegmentKind.LeadIn"/>, just-played track for
/// <see cref="SegmentKind.BackAnnounce"/>, null otherwise.
/// </param>
/// <param name="LocalNow">Local wall-clock time used when rendering time/date copy.</param>
/// <param name="StationId">
/// Stable machine identifier for the station; used to isolate TTS cache entries and
/// file paths per station so that clips from different stations never collide.
/// </param>
/// <param name="PersonaName">
/// Display name of the persona active when this segment was requested, or <see langword="null"/>
/// when none is active (SPEC F39.1). The <c>Orchestrator</c> stamps this from the SAME
/// <c>IActivePersonaAccessor</c> read that resolves <see cref="Voice"/>, so the two always describe
/// the same persona. <c>TtsSegmentSource</c> stamps the produced <see cref="MediaItem.Artist"/> as
/// <c>PersonaName ?? StationName</c> (F39.2); this field carries no meaning outside that seam.
/// </param>
/// <param name="CounterpartName">
/// Display name of the OTHER DJ in a handoff (SPEC F92.2): for <see cref="SegmentKind.SignOff"/>,
/// the incoming DJ taking over; for <see cref="SegmentKind.SignOn"/>, the outgoing DJ just
/// relieved. <see langword="null"/> means no counterpart exists for this boundary (a music-only
/// half of the handoff, F92.3) — every other kind leaves this unset. Additive and optional
/// (default <see langword="null"/>) so every existing caller is diff-free; the F74 queue producer
/// (PLAN T124) is the only writer.
/// </param>
/// <param name="ContextFacts">
/// Plain-text facts for a <see cref="SegmentKind.ContextSegment"/> render (SPEC F107.3, STORY-297,
/// PLAN T224) — <c>GenWave.Core.Domain.ContextContent.SegmentFacts</c> verbatim, never re-derived.
/// The copywriter prompt renders these under the news posture ("read these facts, do not add
/// facts") rather than inventing content of its own. <see langword="null"/> for every other kind —
/// additive and optional (default <see langword="null"/>) so every existing caller is diff-free; the
/// Orchestrator's F74 context-segment drain arm (PLAN T224) is the only writer.
/// </param>
/// <param name="CrossingTrackTitle">
/// SPEC F111.3 (PLAN T235) — for a <see cref="SegmentKind.SignOn"/> held at a straddle seam (SPEC
/// F111.2), the deliberately boundary-crossing track's own title, carried verbatim from the
/// deferral's own captured <c>HandoffContext.CrossingTrackTitle</c> (never re-derived here) so the
/// copywriter prompt can back-announce it. <see langword="null"/> for every other segment — additive
/// and optional (default <see langword="null"/>) so every existing caller is diff-free; the
/// Orchestrator's straddle-assembly drain arm (PLAN T235) is the only writer.
/// </param>
/// <param name="CrossingTrackArtist">
/// The same crossing track's artist, alongside <see cref="CrossingTrackTitle"/> — <see langword="null"/>
/// whenever that is.
/// </param>
/// <param name="ShowName">
/// SPEC F116.2 (STORY-307, PLAN T248) — for a <see cref="SegmentKind.SignOff"/>/<see cref="SegmentKind.SignOn"/>
/// only: this piece's OWN show, carried verbatim from the deferral's own captured
/// <c>HandoffContext.ShowName</c> (never re-derived here). <see langword="null"/> for every other
/// kind, and for a showless boundary — additive and optional so every existing caller is diff-free.
/// </param>
/// <param name="ShowFlavor">
/// SPEC F116.2/F115.3 — <see cref="ShowName"/>'s own flavor text, carried the same way; populated only
/// on a <see cref="SegmentKind.SignOn"/> request (F116.2 names flavor for the sign-on prompt alone).
/// Reaches the LLM prompt ONLY — never a public payload or a log line (F115.3, the persona-soul
/// precedent) — <c>GenWave.Core.Domain.ShowSummary.Flavor</c> and <c>GenWave.Orchestration.HandoffContext.ShowFlavor</c>
/// both carry the same warning this field does: this record's own compiler-generated
/// <c>ToString()</c> renders it verbatim, so no <c>{Request}</c>-style structured-log placeholder may
/// ever bind a <see cref="SegmentRequest"/> on any public-adjacent logging path; log
/// <see cref="PersonaName"/>/<see cref="ShowName"/> by name instead.
/// </param>
/// <param name="CounterpartShowName">
/// SPEC F114.3/F116.2 — the OTHER piece's show, carried the same way; populated only on a
/// <see cref="SegmentKind.SignOff"/> request ("sign-off may name the ending show and the next").
/// </param>
public sealed record SegmentRequest(
    SegmentKind    Kind,
    string         Voice,
    string         StationName,
    MediaItem?     Track,
    DateTimeOffset LocalNow,
    string         StationId,
    string?        PersonaName = null,
    string?        CounterpartName = null,
    string?        ContextFacts = null,
    string?        CrossingTrackTitle = null,
    string?        CrossingTrackArtist = null,
    string?        ShowName = null,
    string?        ShowFlavor = null,
    string?        CounterpartShowName = null);
