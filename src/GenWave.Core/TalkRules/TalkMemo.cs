namespace GenWave.Core.TalkRules;

/// <summary>One rule's private decision, kept as its raw JSON text so equality and storage through <see cref="TalkSnapshotJson"/> are exact.</summary>
public sealed record TalkMemo(string RuleId, string Json);
