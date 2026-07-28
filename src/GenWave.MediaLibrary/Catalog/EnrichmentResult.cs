namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// Everything one enrichment pass extracts from a file in a single open (PRD §8): loudness, the
/// technical audio properties, the normalized tags, cue points (SPEC F13.3), energy levels
/// (STORY-033), and BPM (SPEC F46.3). Written atomically to the catalog row, which then becomes
/// <c>ready</c>. Lives in Catalog (the write payload) so the dependency runs Enrich → Catalog,
/// never the reverse.
///
/// <see cref="Explicit"/> (SPEC F95.2/F95.3, STORY-251, PLAN T112) is the advisory/explicit flag
/// read from the file's own tags — <see langword="null"/> is a miss (no advisory tag present, or
/// present but unparseable), never a stamp; <see cref="MediaRepository.WriteEnrichmentAsync"/>
/// applies the source-'tag' write rule for it exactly once, per that method's own remarks.
/// </summary>
sealed record EnrichmentResult(
    int?      DurationMs,
    int?      SampleRate,
    short?    Channels,
    int?      BitrateKbps,
    string?   Title,
    string?   Artist,
    string?   Album,
    string?   AlbumArtist,
    string?   Genre,
    int?      TrackNo,
    int?      Year,
    bool?     Explicit,
    double    IntegratedLufs,
    double    TruePeakDbtp,
    bool      Measurable,
    double?   CueInSec,
    double?   CueOutSec,
    DateTime? CueAnalyzedAt,
    double?   IntroEnergy,
    double?   OutroEnergy,
    DateTime? EnergyAnalyzedAt,
    double?   Bpm,
    DateTime? BpmAnalyzedAt);
