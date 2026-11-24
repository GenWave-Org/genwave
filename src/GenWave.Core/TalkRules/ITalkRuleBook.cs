namespace GenWave.Core.TalkRules;

/// <summary>Runs the switched-on rules for a kind of talk: shapers, then judges, over the same snapshot.</summary>
public interface ITalkRuleBook
{
    ValueTask<TalkShaping> ShapeAsync(TalkSnapshot before, CancellationToken ct);

    /// <summary>Every rule's verdict, switched-off ones included and marked off; reads the live switches each pass.</summary>
    ValueTask<TalkList<RuleVerdict>> JudgeAsync(TalkSnapshot after, CancellationToken ct);

    /// <summary>Tells every switched-on recording rule about accepted talk; returns the ids of recorders that threw.</summary>
    ValueTask<TalkList<string>> AcceptedAsync(TalkSnapshot accepted, CancellationToken ct);
}
