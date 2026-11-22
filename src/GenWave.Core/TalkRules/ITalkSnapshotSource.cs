namespace GenWave.Core.TalkRules;

/// <summary>Fills a snapshot from one kind's writer inputs plus the utterance store. One per kind, each in its own project.</summary>
public interface ITalkSnapshotSource<in TInput>
{
    ValueTask<TalkSnapshot> BuildAsync(TInput input, CancellationToken ct);
}
