namespace GenWave.Core.TalkRules;

/// <summary>Updates private state once talk is accepted.</summary>
public interface IRecordingTalkRule : ITalkRule
{
    ValueTask AcceptedAsync(TalkSnapshot accepted, CancellationToken ct);
}
