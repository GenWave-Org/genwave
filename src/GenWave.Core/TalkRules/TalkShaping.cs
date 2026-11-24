namespace GenWave.Core.TalkRules;

/// <summary>
/// The rule book's shaping pass: the notes to add to the prompt (sorted by rule id), the memos to carry into the
/// snapshot, and the ids of shapers that threw and so added nothing (SPEC F220.9).
/// </summary>
public sealed record TalkShaping(TalkList<string> PromptNotes, TalkMemos Memos, TalkList<string> CouldNotShape)
{
    /// <summary>The heading of the prompt section the notes go under (SPEC F220.10).</summary>
    public const string ForThisBreakHeading = "For this break:";

    /// <summary>The prompt section for the notes: empty when there are none, else the heading and one line per note.</summary>
    public string ForThisBreak() =>
        PromptNotes.Count == 0
            ? string.Empty
            : string.Join('\n', PromptNotes.Prepend(ForThisBreakHeading));
}
