namespace GenWave.MediaLibrary.Garden;

/// <summary>
/// Flat Dapper projection of <see cref="RotFindingRepository.ListWithMediaAsync"/>'s own joined
/// <c>select</c> (SPEC F153.9; STORY-374; PLAN T377) — the <see cref="RotFindingRow"/> columns plus
/// the <c>library.media</c>/<c>library.media_rotation</c>/<c>library.media_rating</c> columns the
/// admin surface's listing needs alongside each finding, mirrors <see cref="RotFindingRow"/>'s own
/// "one settable-property class per query shape" convention. <c>Kind</c>/<c>State</c> stay raw text
/// (Dapper's global <c>MatchNamesWithUnderscores</c> maps <c>media_id</c>/<c>group_key</c>/...
/// onto these properties, the same way it already maps every other snake_case projection in this
/// codebase); <see cref="RotFindingRepository"/> parses them into <c>RotKind</c>/<c>RotState</c>
/// before handing a <c>GenWave.Core.Domain.RotFindingWithMedia</c> back to its own caller.
/// </summary>
sealed class RotFindingWithMediaRow
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
    public string Locator { get; set; } = "";
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public int? DurationMs { get; set; }
    public int Plays { get; set; }
    public int? Rating { get; set; }
    public bool NeverPlay { get; set; }
    public bool Eligible { get; set; }
}
