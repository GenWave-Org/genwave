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
/// </summary>
public sealed record SafeSegmentCreateRequest(
    string? Text,
    long? LibraryId,
    string? Title = null,
    string? Voice = null,
    long? BedMediaId = null,
    string? Kind = null);
