namespace GenWave.Core.Domain;

/// <summary>
/// Outcome of the catalog's mood write path (<c>MediaRepository.WriteMoodsAsync</c>, SPEC F85.1,
/// F85.2; STORY-216). Mirrors <see cref="RatingWriteResult"/>'s shape (no scope gate, no version
/// conflict on this path) plus the two rejection cases the mood vocabulary gate introduces — an
/// out-of-vocabulary term or a set larger than <see cref="MoodVocabulary.MaxMoodsPerTrack"/> reject
/// the WHOLE write, never a partial one (the same "never a partial write" lesson F85.4 applies to
/// the tagger's own parse step).
/// </summary>
public enum MoodWriteResult
{
    /// <summary>The media row exists and the moods were written.</summary>
    Written,

    /// <summary>At least one entry is not a member of <see cref="MoodVocabulary.Terms"/> — nothing
    /// was written (SPEC F85.1).</summary>
    UnknownMood,

    /// <summary>More than <see cref="MoodVocabulary.MaxMoodsPerTrack"/> entries were supplied —
    /// nothing was written (SPEC F85.2).</summary>
    TooManyMoods,

    /// <summary>No row with the given media id exists in <c>library.media</c>.</summary>
    NotFound,
}
