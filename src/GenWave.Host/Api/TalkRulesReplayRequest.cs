namespace GenWave.Host.Api;

/// <summary>Body of <c>POST /api/talk-rules/replay</c>: <paramref name="Days"/> 1–90, optional <paramref name="Compare"/> ("legacy"), optional <paramref name="Kinds"/> ("dj-line", "chat", "ad"; default all).</summary>
public sealed record TalkRulesReplayRequest(int Days, string? Compare = null, IReadOnlyList<string>? Kinds = null);
