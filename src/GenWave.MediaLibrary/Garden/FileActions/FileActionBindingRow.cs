namespace GenWave.MediaLibrary.Garden.FileActions;

/// <summary>
/// The row facts <see cref="FileActionRepository.ReadBindingAsync"/> re-reads right before an
/// executor attempt (SPEC F154.5; STORY-379 AC7's executor half; PLAN T380, gh-#529) — the TOCTOU
/// re-check between dry-run/confirm and the moment the gate is actually held. Exactly the two fields
/// <c>Core.Domain.PlanBinding.Matches</c> needs (T380 review N7): an earlier draft also carried
/// <c>size_bytes</c>/<c>mtime</c> "for the same round trip," but nothing ever read them — a caller
/// that later wants them can add them back with a real consumer, not speculatively.
/// </summary>
sealed record FileActionBindingRow(string Xmin, string Path);
