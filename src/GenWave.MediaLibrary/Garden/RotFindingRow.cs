namespace GenWave.MediaLibrary.Garden;

/// <summary>
/// Flat Dapper projection of <see cref="RotFindingRepository.ListAsync"/>'s own <c>select</c> (SPEC
/// F153.1, F153.9; STORY-374; PLAN T372) — mirrors <c>RotationHealthCountsRow</c>'s own "one
/// settable-property class per query shape" convention (a bare positional value tuple is not itself
/// a house type, one type per file). <c>Kind</c>/<c>State</c> stay raw text here — Dapper's global
/// <c>MatchNamesWithUnderscores</c> maps <c>media_id</c>/<c>group_key</c>/... onto these properties
/// the same way it already maps every other snake_case projection in this codebase;
/// <see cref="RotFindingRepository"/> parses them into <c>RotKind</c>/<c>RotState</c> before handing
/// a <c>GenWave.Core.Domain.RotFinding</c> back to its own caller.
/// </summary>
sealed class RotFindingRow
{
    public long Id { get; set; }
    public long MediaId { get; set; }
    public string Kind { get; set; } = "";
    public string State { get; set; } = "";
    public string? GroupKey { get; set; }
    public string Evidence { get; set; } = "";
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? DismissedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
