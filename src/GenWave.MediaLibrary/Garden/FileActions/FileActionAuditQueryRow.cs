namespace GenWave.MediaLibrary.Garden.FileActions;

/// <summary>
/// Flat Dapper projection of <see cref="FileActionRepository.ListAuditAsync"/>'s own <c>select</c>
/// (SPEC F154.7; STORY-379; PLAN T380, gh-#529) — mirrors <c>RotFindingRow</c>'s own "one
/// settable-property class per query shape" convention. <see cref="Verb"/> stays raw text here —
/// Dapper's global <c>MatchNamesWithUnderscores</c> maps <c>from_path</c>/<c>plan_token</c>/... onto
/// these properties the same way it already maps every other snake_case projection in this codebase;
/// <see cref="FileActionRepository"/> parses <see cref="Verb"/> into a
/// <c>GenWave.Core.Domain.FileActionVerb</c> before handing a <see cref="FileActionAuditRecord"/>
/// back to its own caller. <see cref="Detail"/> stays opaque text (<c>detail::text</c>) — the same
/// <c>FontPack.Definition</c>/<c>rot_finding.evidence</c> precedent — this class never parses it.
/// </summary>
sealed class FileActionAuditQueryRow
{
    public long Id { get; set; }
    public long MediaId { get; set; }
    public string Verb { get; set; } = "";
    public string FromPath { get; set; } = "";
    public string? ToPath { get; set; }
    public string PlanToken { get; set; } = "";
    public DateTimeOffset PerformedAt { get; set; }
    public string Outcome { get; set; } = "";
    public string Detail { get; set; } = "{}";
}
