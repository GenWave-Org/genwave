namespace GenWave.Core.TalkRules;

/// <summary>A judge's objection: <paramref name="Reason"/> for people, <paramref name="Ask"/> one sentence for the model, <paramref name="Evidence"/> the words.</summary>
public sealed record RuleFlag(string Reason, string Ask, string Evidence);
