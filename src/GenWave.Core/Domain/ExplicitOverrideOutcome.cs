namespace GenWave.Core.Domain;

/// <summary>
/// Outcome of <see cref="Abstractions.IMediaExplicitOverride.SetExplicitOverrideAsync"/> (SPEC
/// F95.3, STORY-251, PLAN T115). <see cref="Explicit"/>/<see cref="ExplicitSource"/> are populated
/// only when <see cref="Result"/> is <see cref="ExplicitOverrideResult.Updated"/> — the row's
/// post-write values, read straight from the UPDATE's <c>RETURNING</c> clause (no second read).
/// Both are <see langword="null"/> after a clear (mirrors a fresh, unclassified row).
/// </summary>
public readonly record struct ExplicitOverrideOutcome(
    ExplicitOverrideResult Result,
    bool? Explicit,
    string? ExplicitSource);
