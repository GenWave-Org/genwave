namespace GenWave.Tts;

/// <summary>
/// The writer's two pieces <see cref="ChatRulesFlow"/> reuses rather than copies: <paramref name="Ask"/> posts one completion
/// (attempt number, the prompts) and parses the reply, and <paramref name="RecordCaught"/> writes the ring entry of a reply a rule
/// flagged (its raw text), before the re-ask fires. The last attempt's entry is the writer's own, as in legacy.
/// </summary>
internal sealed record ChatRulesCalls(
    Func<int, BreakPrompt, Task<ChatReply>> Ask,
    Action<string> RecordCaught);
