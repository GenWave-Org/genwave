namespace GenWave.Core.TalkRules;

/// <summary>One rule's private decision, kept as its canonical JSON text (see <see cref="TalkMemos"/>), so equality and storage through <see cref="TalkSnapshotJson"/> are exact, even after a jsonb round trip.</summary>
internal sealed record TalkMemo(string RuleId, string Json);
