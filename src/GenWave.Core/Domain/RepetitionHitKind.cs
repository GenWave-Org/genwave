namespace GenWave.Core.Domain;

/// <summary>Which rule produced a <see cref="RepetitionHit"/>.</summary>
public enum RepetitionHitKind
{
    /// <summary>A shared run, a spent quirk or an unavailable tagline (F218.11, F218.33).</summary>
    Phrase,

    /// <summary>One content word the persona already used in several lines this shift (PLAN T724, STORY-534).</summary>
    Motif,
}
