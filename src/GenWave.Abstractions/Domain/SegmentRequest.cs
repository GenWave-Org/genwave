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
public sealed record SegmentRequest(
    SegmentKind    Kind,
    string         Voice,
    string         StationName,
    MediaItem?     Track,
    DateTimeOffset LocalNow,
    string         StationId,
    string?        PersonaName = null);
