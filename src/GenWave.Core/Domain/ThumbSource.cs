namespace GenWave.Core.Domain;

/// <summary>
/// Who posted a thumb (SPEC F150.7; STORY-371, STORY-369; PLAN T365): a spectator on the Live page,
/// or the operator posting on their behalf. Maps 1:1 to <c>library.thumb_source</c>
/// (<c>'spectator'</c>/<c>'operator'</c>) by lowercase text — the same mapping discipline
/// <see cref="ThumbDirection"/>'s own remarks describe. F150.7's own rule: operator thumbs always
/// carry <c>listener_key = "operator"</c> alongside <see cref="Operator"/>.
/// </summary>
public enum ThumbSource
{
    /// <summary>A spectator on the Live page.</summary>
    Spectator,

    /// <summary>The operator, posting on behalf of the station.</summary>
    Operator,
}
