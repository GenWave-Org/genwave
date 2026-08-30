namespace GenWave.Core.Domain;

/// <summary>
/// One tag field a retag plan will change (SPEC F154.1, F154.5; STORY-379; PLAN T379, gh-#529) — the
/// catalog value always wins; a <see langword="null"/> catalog value never produces a
/// <see cref="TagChange"/> at all (never blanks a tag the catalog simply doesn't carry an opinion
/// on). <see cref="Field"/> is a lower-case token (<c>"artist"</c>, <c>"title"</c>, <c>"album"</c>,
/// <c>"year"</c>, <c>"genre"</c>) — the same casing the dry-run response and audit row will carry.
/// </summary>
/// <param name="Field">Which tag field changes — one of <c>artist</c>/<c>title</c>/<c>album</c>/
/// <c>year</c>/<c>genre</c>.</param>
/// <param name="FileValue">The file's current value for <see cref="Field"/>, or
/// <see langword="null"/> when the file carries none.</param>
/// <param name="CatalogValue">The catalog's value for <see cref="Field"/> — non-nullable ON
/// PURPOSE (T380 review N1): the planner never emits a <see cref="TagChange"/> for a null/blank
/// catalog value in the first place (<c>Garden.FileActions.TagDiffCalculator</c>'s own guard), so
/// making this non-nullable makes that state unrepresentable at the type level rather than a
/// doc-only promise a future caller could violate.</param>
public sealed record TagChange(string Field, string? FileValue, string CatalogValue);
