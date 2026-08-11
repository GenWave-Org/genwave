namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /api/safe-segments</c> (F27.3, STORY-079).
///
/// <see cref="Text"/> and <see cref="LibraryId"/> are required — both are nullable here so the
/// controller can produce a typed 400 ProblemDetails instead of a model-binder 400 when a field is
/// missing or explicitly null (mirrors <see cref="BulkReassignRequest.ToLibraryId"/>).
/// <see cref="Title"/>, <see cref="Voice"/>, and <see cref="BedMediaId"/> are optional; defaults are
/// applied by <see cref="SafeSegmentsController"/> / <c>GenWave.Tts.SafeSegmentAuthor</c>.
///
/// <see cref="Kind"/> (gh-#149) is the optional Station Imaging content kind token —
/// <c>liner</c>/<c>station_id</c>/<c>jingle</c>/<c>promo</c>, parsed case-insensitively by
/// <c>ImagingKindTokens.TryParse</c>. Absent/null defaults to <c>liner</c> (today's behavior);
/// an unknown value is a 400, nothing rendered. Metadata-only: it never changes how the segment
/// renders or plays.
///
/// <see cref="ShowId"/> (SPEC F117.1, STORY-313, PLAN T246) is the optional show scope the
/// authoring UI's scope picker chose — station-wide when absent/null (today's only behavior for
/// every pre-F117 request). A non-null value referencing no <c>station.show</c> row is a 400,
/// nothing rendered, mirroring <see cref="LibraryId"/>/<see cref="BedMediaId"/>'s own
/// validate-first discipline. The UI gates its own picker to the <c>station_id</c> kind (F119.4),
/// but this field itself is not kind-restricted server-side — the write seam it rides
/// (<c>IAuthoredCatalogWriter.InsertAuthoredAsync</c>) accepts a scope on any kind; a scope on a
/// kind no consumer reads yet is simply unread, never rejected.
/// </summary>
public sealed record SafeSegmentCreateRequest(
    string? Text,
    long? LibraryId,
    string? Title = null,
    string? Voice = null,
    long? BedMediaId = null,
    string? Kind = null,
    long? ShowId = null);
