using System.Globalization;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging;
using LoudnessMeasurement = GenWave.Core.Domain.Loudness;

namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// Flat Dapper projection of a <c>library.media</c> row (snake_case mapped via
/// <c>MatchNamesWithUnderscores</c>). Enrichment columns are nullable until the file is enriched, so
/// they are nullable here and collapse to sensible defaults when narrowed to a <see cref="MediaReference"/>.
/// </summary>
class MediaRow
{
    public long Id { get; set; }
    public string Path { get; set; } = "";
    public string Format { get; set; } = "";
    public string State { get; set; } = "discovered";
    public string? Title { get; set; }
    public int? DurationMs { get; set; }
    public int? SampleRate { get; set; }
    public short? Channels { get; set; }
    public int? BitrateKbps { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? Genre { get; set; }
    public int? Year { get; set; }
    public double? IntegratedLufs { get; set; }
    public double? TruePeakDbtp { get; set; }
    public bool? Measurable { get; set; }
    public double? CueInSec { get; set; }
    public double? CueOutSec { get; set; }
    public double? IntroEnergy { get; set; }
    public double? OutroEnergy { get; set; }
    public bool Eligible { get; set; } = true;

    /// <summary>Tempo estimate (SPEC F46.1); null until BPM analysis has run.</summary>
    public double? Bpm { get; set; }

    /// <summary>
    /// Whole-track perceptual energy — the STORED generated column (SPEC F47.1); null until
    /// <c>integrated_lufs</c> is measured. Only populated by projections that select it
    /// (the admin queries); <see cref="ToReference"/> never reads it.
    /// </summary>
    public double? TrackEnergy { get; set; }

    /// <summary>
    /// Rating state (SPEC F33.10), populated only by the admin queries that LEFT JOIN
    /// <c>library.media_rating</c> and project <c>coalesce(r.score, 50) as score</c>. Queries that
    /// don't select this column (playout's <see cref="ToReference"/> path) simply leave it at the
    /// F33.2 ledger default — those projections never read it.
    /// </summary>
    public int Score { get; set; } = 50;

    /// <summary>Companion to <see cref="Score"/> — see that member's remarks.</summary>
    public bool NeverPlay { get; set; }

    /// <summary>
    /// gh-#99 — false when the row's <c>library_id</c> falls in the live safe scope (projected as
    /// <c>not (m.library_id = any(@safeLibraryIds))</c> by the admin list query). Defaults true for
    /// every projection that doesn't select it, mirroring <see cref="Score"/>'s posture.
    /// </summary>
    public bool Rateable { get; set; } = true;

    /// <summary>
    /// Mood tags (SPEC F85.1/F86.8) — <c>library.media.moods</c>, a Postgres <c>text[]</c>. Null
    /// until the mood tagger reaches (or misses on) the row; only populated by projections that
    /// select it (the admin queries), mirroring <see cref="TrackEnergy"/>'s pattern.
    /// </summary>
    public string[]? Moods { get; set; }

    /// <summary>
    /// Postgres system column <c>xmin</c> — the transaction id that last wrote this row.
    /// Exposed as a string for use as an optimistic-concurrency token (ETag) on the admin write path.
    /// Dapper maps this because <c>MatchNamesWithUnderscores</c> is enabled globally and the column is
    /// selected explicitly as <c>xmin::text as xmin</c>.
    /// </summary>
    public string Xmin { get; set; } = "";

    /// <summary>
    /// Projects cue columns onto a <see cref="CuePoints"/> value. Both non-null → cue; both null →
    /// null; asymmetric → null with a WARN log (data-integrity signal).
    /// </summary>
    public CuePoints? ResolveCue(ILogger logger)
    {
        if (CueInSec.HasValue && CueOutSec.HasValue)
        {
            if (CueInSec.Value >= CueOutSec.Value)
            {
                logger.LogWarning("Media row {Id} has inverted cue columns (in={CueIn}, out={CueOut}) — treating as no cue",
                    Id, CueInSec.Value, CueOutSec.Value);
                return null;
            }
            return new CuePoints(CueInSec.Value, CueOutSec.Value);
        }

        if (!CueInSec.HasValue && !CueOutSec.HasValue)
            return null;

        logger.LogWarning("Media row {Id} has asymmetric cue columns — treating as no cue", Id);
        return null;
    }

    /// <summary>
    /// Projects energy columns onto nullable pair. Both non-null → values; both null → null, null;
    /// asymmetric (exactly one null) → null, null with a WARN log (data-integrity signal).
    /// </summary>
    public (double? intro, double? outro) ResolveEnergy(ILogger logger)
    {
        if (IntroEnergy.HasValue && OutroEnergy.HasValue)
            return (IntroEnergy.Value, OutroEnergy.Value);

        if (!IntroEnergy.HasValue && !OutroEnergy.HasValue)
            return (null, null);

        logger.LogWarning("Media row {Id} has asymmetric energy columns — treating as no energy", Id);
        return (null, null);
    }

    public MediaReference ToReference(ILogger logger)
    {
        var (intro, outro) = ResolveEnergy(logger);
        return new(
            MediaId: Id.ToString(CultureInfo.InvariantCulture),
            Locator: Path,
            Title: Title ?? System.IO.Path.GetFileNameWithoutExtension(Path),
            Loudness: new LoudnessMeasurement(IntegratedLufs ?? 0.0, TruePeakDbtp ?? 0.0, Measurable ?? false),
            DurationMs: DurationMs,
            SampleRate: SampleRate,
            Channels: Channels,
            BitrateKbps: BitrateKbps,
            Artist: Artist,
            Album: Album,
            Genre: Genre,
            Year: Year,
            Cue: ResolveCue(logger),
            IntroEnergy: intro,
            OutroEnergy: outro);
    }

    public AdminMediaDto ToAdminDto() => new(
        MediaId: Id.ToString(CultureInfo.InvariantCulture),
        Locator: Path,
        Format: Format,
        State: State,
        DurationMs: DurationMs,
        Title: Title,
        Artist: Artist,
        Album: Album,
        Genre: Genre,
        Year: Year,
        IntegratedLufs: IntegratedLufs,
        TruePeakDbtp: TruePeakDbtp,
        Measurable: Measurable,
        CueInSec: CueInSec,
        CueOutSec: CueOutSec,
        Eligible: Eligible,
        Version: Xmin,
        Score: Score,
        NeverPlay: NeverPlay,
        Bpm: Bpm,
        TrackEnergy: TrackEnergy,
        Moods: Moods,
        Rateable: Rateable);
}
