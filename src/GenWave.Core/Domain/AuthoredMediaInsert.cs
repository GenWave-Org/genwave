namespace GenWave.Core.Domain;

/// <summary>
/// Everything the authored-insert seam (<see cref="Abstractions.IAuthoredCatalogWriter"/>) needs to
/// land a fully-measured, brand-tagged <c>library.media</c> row directly in <c>state='ready'</c> for
/// a generated safe-segment artifact (F27.1, F27.2, F27.8). Every measurement here was taken on the
/// generated OUTPUT file by the same analyzers the enricher uses — there is no enricher round-trip,
/// so the caller (P5's <c>SafeSegmentAuthor</c>) supplies the finished results up front.
/// </summary>
/// <param name="Path">Engine-visible path under <c>Station:Safe:AuthoredRoot</c> (F11.12, F27.1).</param>
/// <param name="Format">File container/codec (e.g. <c>"wav"</c>).</param>
/// <param name="LibraryId">
/// Destination library. An id that references no row in <c>library.library</c> is rejected by the
/// existing foreign key (SQLSTATE 23503) — the insert writes nothing (F27.1 all-or-nothing).
/// </param>
/// <param name="SizeBytes">
/// The artifact's file size, stat'd by the caller after writing it — the same fingerprint field
/// discovery populates for scanned files.
/// </param>
/// <param name="Mtime">The artifact's file modification time, stat'd by the caller.</param>
/// <param name="Tags">
/// Brand tags for the row's tag columns (artist = <c>Station:Name</c>, title = the request's title,
/// F27.2). <see cref="Tags"/> land on tag columns whose <c>tags_edited_at</c> is stamped in the same
/// insert, freezing them against re-scan/backfill exactly like an operator edit (F18.3 semantics).
/// </param>
/// <param name="Loudness">Integrated LUFS / true-peak / measurable, measured on the artifact (F27.1).</param>
/// <param name="Cue">
/// Cue points measured on the artifact, or null when the analyzer found no trim boundaries. Either
/// way <c>cue_analyzed_at</c> is stamped so F13's backfill predicate never re-claims this row.
/// </param>
/// <param name="Energy">
/// Intro/outro energy measured on the artifact, or null when the analyzer found none. Either way
/// <c>energy_analyzed_at</c> is stamped so F17's backfill predicate never re-claims this row.
/// </param>
/// <param name="DurationMs">Duration in milliseconds, when the caller has it.</param>
/// <param name="SampleRate">Sample rate in Hz, when the caller has it.</param>
/// <param name="Channels">Channel count, when the caller has it.</param>
/// <param name="BitrateKbps">Bitrate in kbps, when the caller has it.</param>
public sealed record AuthoredMediaInsert(
    string Path,
    string Format,
    long LibraryId,
    long SizeBytes,
    DateTime Mtime,
    AudioTags Tags,
    Loudness Loudness,
    CuePoints? Cue,
    EnergyPoints? Energy,
    int? DurationMs,
    int? SampleRate,
    short? Channels,
    int? BitrateKbps);
