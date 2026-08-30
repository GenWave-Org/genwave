namespace GenWave.MediaLibrary.Garden.FileActions;

/// <summary>
/// The raw columns <see cref="FileActionRepository.ReadSubjectAsync"/> selects (SPEC F154.1, F154.3;
/// STORY-379; PLAN T381, gh-#529) — mapped onto a <c>Core.Domain.FileActionSubject</c> by the
/// repository itself (<see cref="MediaId"/> is the caller's own parameter, never selected — every
/// other field here is a plain <c>library.media</c> column).
/// </summary>
sealed record FileActionSubjectRow(
    string Xmin, string Path, long LibraryId,
    string? Artist, string? Title, string? Album, int? Year, string? Genre);
