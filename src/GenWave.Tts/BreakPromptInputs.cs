namespace GenWave.Tts;

using GenWave.Core.Domain;

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
}
