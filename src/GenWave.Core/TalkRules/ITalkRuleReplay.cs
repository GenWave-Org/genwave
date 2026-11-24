namespace GenWave.Core.TalkRules;

/// <summary>Re-judges stored snapshots with today's rules: per-rule counts and changed verdicts.</summary>
public interface ITalkRuleReplay
{
    ValueTask<TalkReplayReport> ReplayAsync(TalkReplayRequest request, CancellationToken ct);
}
