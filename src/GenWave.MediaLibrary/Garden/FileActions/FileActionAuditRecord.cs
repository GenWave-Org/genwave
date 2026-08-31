using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Garden.FileActions;

/// <summary>
/// One row read back from <c>library.file_action</c> (SPEC F154.7; STORY-379; PLAN T380, gh-#529) —
/// <see cref="FileActionRepository.ListAuditAsync"/>'s own element type, parsed out of
/// <see cref="FileActionAuditQueryRow"/>. Test-support only today (no production caller reads the
/// audit trail back yet); T381's own Host-level history surface, if any, defines its own DTO at its
/// own edge rather than reusing this one.
/// </summary>
sealed record FileActionAuditRecord(
    long Id,
    long MediaId,
    FileActionVerb Verb,
    string FromPath,
    string? ToPath,
    string PlanToken,
    DateTimeOffset PerformedAt,
    string Outcome,
    string Detail);
