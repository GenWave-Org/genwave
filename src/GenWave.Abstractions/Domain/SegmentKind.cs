namespace GenWave.Core.Domain;

/// <summary>
/// Identifies the role a TTS segment plays in the broadcast flow.
/// </summary>
public enum SegmentKind
{
    /// <summary>Short station identification ("You're listening to…").</summary>
    StationId,

    /// <summary>Introduces an upcoming track before it plays.</summary>
    LeadIn,

    /// <summary>Names a track immediately after it finishes.</summary>
    BackAnnounce,

    /// <summary>Announces the current local time and date.</summary>
    TimeDate,

    /// <summary>Outgoing DJ closes out their shift at a roster boundary (SPEC F92.2).</summary>
    SignOff,

    /// <summary>Incoming DJ opens their shift at a roster boundary (SPEC F92.2).</summary>
    SignOn,

    /// <summary>
    /// A context provider's fact-based segment (SPEC F107.3, STORY-297) — weather, this-day-in-
    /// history, or any future <c>IContextProvider</c> kind. Drained from a
    /// <c>SpeechDeferralKind.Context</c> deferral at the next track boundary (F74.1); the wiring
    /// that actually produces a <c>SegmentRequest</c> of this kind is T224's, not this enum's own.
    /// </summary>
    ContextSegment,

    /// <summary>
    /// A banter exchange in which two personas share one clip (SPEC F127.1, STORY-329) — a
    /// mid-block-seam voice moment, superseding the gated flavor/fact lanes in any break it
    /// airs. Additive member; the wiring that vends a <c>SegmentRequest</c> of this kind is
    /// T287's, not this enum's own.
    /// </summary>
    Crosstalk,
}
