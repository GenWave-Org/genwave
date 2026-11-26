namespace GenWave.Tts;

using GenWave.Core.Domain;
using GenWave.Core.TalkRules;

/// <summary>
/// Everything <see cref="BreakPromptComposer"/> needs to compose one break's prompt (SPEC F218.27).
/// Nondeterminism (the self-name roll, the quirk pick, the clock) arrives already resolved, so the
/// composition stays a pure function of this record.
/// </summary>
internal sealed record BreakPromptInputs(
    SegmentRequest Request,
    Persona? Persona,
    PersonaCard? Card,
    bool MentionOwnName,
    QuirkSelection? Quirks,
    IReadOnlyList<string> RecentLines,
    int MaxCopyChars,
    DateTimeOffset StationLocalNow,
    IReadOnlyList<string> PreviouslyVoicedTasteNotes)
{
    /// <summary>The owner's announcement text; non-null only for an announcement render (F144.3).</summary>
    public string? AnnouncementMessage { get; init; }

    /// <summary>The due context fact, when the break carries one (F107.5).</summary>
    public string? PatterFact { get; init; }

    /// <summary>The due show-flavor line, when the break carries one (F116.3).</summary>
    public ShowFlavorFact? ShowFlavor { get; init; }

    /// <summary>Tagline statuses for an on-air break (F218.32); empty for a preview or the bench.</summary>
    public IReadOnlyList<TaglineStatus> Taglines { get; init; } = [];

    /// <summary>What this on-air break remembers (F218.22); null or empty adds no block.</summary>
    public MemoryPacket? Memory { get; init; }

    /// <summary>The sound tags this break may carry (F219.16); null adds none.</summary>
    public SoundTagOffer? SoundTags { get; init; }

    /// <summary>The one memory this lead-in or back-announce is asked to work in (F218.40); null adds no ask.</summary>
    public CallbackAsk? Callback { get; init; }

    /// <summary>
    /// The rules-mode form (SPEC F220.10, F220.21): when set, the quirks block and the recent-lines block are not composed
    /// here, because the shapers already produced them; their notes ride in the "For this break" section instead.
    /// <see cref="Quirks"/>, <see cref="RecentLines"/>, <see cref="Taglines"/>, <see cref="Memory"/> (the never-mention list),
    /// <see cref="Callback"/> (the ask), <see cref="MentionOwnName"/> (the name line), <see cref="PreviouslyVoicedTasteNotes"/> (the
    /// taste repeat note), <see cref="ShowFlavor"/> (the show note) and <see cref="StationLocalNow"/> as the clock guard line
    /// (the station clock line in the user prompt stays) are then ignored. Null is today's composition.
    /// </summary>
    public TalkShaping? Shaping { get; init; }
}
