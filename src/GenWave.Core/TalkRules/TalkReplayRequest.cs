namespace GenWave.Core.TalkRules;

/// <summary>What to replay: kinds and window; <paramref name="CompareLegacy"/> adds the legacy-vs-rules comparison (F220.20).</summary>
public sealed record TalkReplayRequest(TalkKinds Kinds, DateTimeOffset Since, bool CompareLegacy);
