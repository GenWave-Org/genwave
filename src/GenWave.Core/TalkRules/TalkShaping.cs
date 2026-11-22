namespace GenWave.Core.TalkRules;

/// <summary>The rule book's shaping pass: the notes to add to the prompt, and the memos to carry into the snapshot.</summary>
public sealed record TalkShaping(TalkList<string> PromptNotes, TalkMemos Memos);
