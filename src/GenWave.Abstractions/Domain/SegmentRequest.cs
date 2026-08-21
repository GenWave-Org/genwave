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
/// PLAN T224) — <c>GenWave.Core.Domain.ContextSegmentFacts.SegmentFacts</c> verbatim, never
/// re-derived: the pipeline's own vend-time window join over the provider's airable
/// <c>ContextContent.Facts</c> (SPEC F125.2/F125.3).
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
/// SPEC F116.2 (STORY-307, PLAN T248) — for a <see cref="SegmentKind.SignOff"/>/<see cref="SegmentKind.SignOn"/>:
/// this piece's OWN show, carried verbatim from the deferral's own captured
/// <c>HandoffContext.ShowName</c> (never re-derived here). <see langword="null"/> for a showless
/// boundary — additive and optional so every existing caller is diff-free.
///
/// <para>
/// SPEC F117.2 (STORY-309, PLAN T250) — ALSO rides on a <see cref="SegmentKind.StationId"/> request
/// when the drain is firing during a show and the authored imaging pool came up empty: the
/// Orchestrator's own drain arm stamps the on-air show's name here, and
/// <c>GenWave.Tts.PatterTemplateRenderer.Expand</c>'s <see cref="SegmentKind.StationId"/> arm
/// renders "You're listening to {ShowName} on {StationName}." instead of the plain
/// "You're listening to {StationName}." <see langword="null"/> (the F110.2-original, byte-identical
/// phrasing) outside a show, or whenever an authored pool row already served the drain.
/// </para>
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
    string?        CounterpartShowName = null)
{
    /// <summary>
    /// SPEC F127.9 (STORY-329, PLAN T287) — "banter supersedes": <see langword="true"/> on a
    /// <see cref="SegmentKind.LeadIn"/>/<see cref="SegmentKind.BackAnnounce"/> request built for the SAME
    /// break a <see cref="SegmentKind.Crosstalk"/> exchange is vending into, so
    /// <c>GenWave.Tts.LlmCopyWriter</c>'s shared-slot arbitration (SPEC F107.5/F116.3) never even ASKS its
    /// context-fact/show-flavor seams for that render — one voice-moment per break. <see langword="false"/>
    /// (the default) for every other request, including every pre-F127 caller, so this field is diff-free
    /// for the whole codebase until <c>Orchestrator.EnqueuePatterAsync</c>'s own crosstalk vend step (the
    /// ONLY writer) stamps it true.
    ///
    /// <para>
    /// <b>Declared as a defaulted body property, not a 15th primary-constructor parameter (round-2
    /// review F1 — the exact T285-round-3 defect, <see cref="ShowSummary.Slug"/>'s own precedent).</b>
    /// This record already shipped inside the Abstractions 5.0.0 NuGet with a 14-arg <c>ctor</c> and
    /// 14-arity <c>Deconstruct</c>; adding a further positional parameter would silently delete both
    /// from the published binary surface, breaking every compiled caller regardless of the new
    /// parameter's own default value. Every construction site that needs to set this uses an
    /// object-initializer/<c>with</c> expression, never a positional/named constructor argument.
    /// </para>
    /// </summary>
    public bool CrosstalkAiredThisBreak { get; init; }

    /// <summary>
    /// SPEC F141.2/F141.3 (STORY-355, PLAN T326) — for a <see cref="SegmentKind.TimeDate"/> request,
    /// how honestly this drain can speak the hour it was armed for; see
    /// <see cref="TimeAnnouncementFreshness"/>'s own remarks for the full contract.
    /// <see cref="TimeAnnouncementFreshness.OnTime"/> (the default) for every other kind, and for
    /// every pre-F141 caller, so this field is diff-free for the whole codebase until the
    /// Orchestrator's own <c>TimeDate</c> drain arm (the ONLY writer) stamps it.
    ///
    /// <para>
    /// <b>Declared as a defaulted body property, not a 16th primary-constructor parameter</b> — the
    /// SAME <see cref="CrosstalkAiredThisBreak"/> precedent immediately above, for the identical
    /// published-NuGet-arity reason (that member's own remarks carry the full rationale).
    /// </para>
    /// </summary>
    public TimeAnnouncementFreshness TimeDateFreshness { get; init; } = TimeAnnouncementFreshness.OnTime;
}
