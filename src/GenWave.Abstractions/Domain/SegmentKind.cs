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
}
