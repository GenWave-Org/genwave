namespace GenWave.Host.Tests.Support;

/// <summary>The JSON <c>POST /api/talk-rules/replay</c> answers with, as a spec reads it.</summary>
internal sealed record ReplayBody(List<ReplayCount> Counts, List<ReplayChange> Changes, int TrimSavedLines);

internal sealed record ReplayCount(string RuleId, int Checked, int Flagged);

internal sealed record ReplayChange(long CheckId, string RuleId, ReplayFlag? Stored, ReplayFlag? Replayed, string? LegacyOutcome);

internal sealed record ReplayFlag(string Reason, string Ask, string Evidence);
