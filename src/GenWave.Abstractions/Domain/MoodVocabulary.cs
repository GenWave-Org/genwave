namespace GenWave.Core.Domain;

/// <summary>
/// The fixed, versioned mood vocabulary (SPEC F85.1, STORY-216) — the single term list a mood tag
/// is ever drawn from. Living in <c>GenWave.Abstractions</c> (not <c>GenWave.MediaLibrary</c> or
/// <c>GenWave.Core</c>) is deliberate: community persona cards and the Q1'27 request-matching
/// feature need to speak the exact same mood language as the catalog, so the list belongs in the
/// SDK contract package every consumer already references, not behind the library's own write path.
/// <para>
/// There is no per-station dialect and no free-form model output — <see cref="Terms"/> is the whole
/// alphabet. The catalog's write path (<c>MediaRepository.WriteMoodsAsync</c>, STORY-216, T58)
/// rejects any write containing a term outside this set (F85.1) or more than
/// <see cref="MaxMoodsPerTrack"/> entries (F85.2) before it ever reaches Postgres.
/// </para>
/// <para>
/// Version 1 (2026-07-21): 16 lowercase, single-word, mutually distinct terms chosen to span both
/// axes a radio programmer cares about — energy (<c>driving</c>/<c>raucous</c> vs.
/// <c>brooding</c>/<c>somber</c>) and valence (<c>playful</c>/<c>warm</c>/<c>triumphant</c> vs.
/// <c>cold</c>/<c>tense</c>/<c>gritty</c>) — so no reasonable track is left without an adequate
/// label. A future version only ever APPENDS terms; nothing already stored needs rewriting when it
/// does (existing rows stay valid subsets of the new, larger set).
/// </para>
/// </summary>
public static class MoodVocabulary
{
    /// <summary>
    /// The vocabulary's version. Bumped only when <see cref="Terms"/> gains (never loses or
    /// renames) an entry — a removal/rename would strand already-stored rows outside the set this
    /// class defines as valid, which SPEC F85.1 never permits.
    /// </summary>
    public const int Version = 1;

    /// <summary>F85.2 — a track carries at most this many mood tags.</summary>
    public const int MaxMoodsPerTrack = 3;

    /// <summary>
    /// The term list itself (SPEC F85.1). Every entry is lowercase and a single word (no spaces, no
    /// hyphens) — the exact shape the tagger's constrained-output contract (F85.4) and any future
    /// consumer (a card import, a request-matching parser) can rely on without a normalization step.
    /// </summary>
    public static readonly IReadOnlyList<string> Terms =
    [
        "dreamy", "driving", "somber", "playful", "tense", "warm", "cold", "epic",
        "intimate", "gritty", "breezy", "brooding", "triumphant", "wistful", "hypnotic", "raucous",
    ];

    static readonly HashSet<string> lookup = new(Terms, StringComparer.Ordinal);

    /// <summary>
    /// Whether <paramref name="term"/> is an exact, case-sensitive member of <see cref="Terms"/>.
    /// Case-sensitive on purpose: the vocabulary is defined as already-lowercase, so any caller
    /// producing mixed case (a tagger, an operator) normalizes before asking — this method is the
    /// single source of truth for "is this literally a vocabulary word," not a fuzzy match.
    /// </summary>
    public static bool Contains(string term) => lookup.Contains(term);
}
