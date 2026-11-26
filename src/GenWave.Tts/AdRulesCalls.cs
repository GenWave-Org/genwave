namespace GenWave.Tts;

/// <summary>
/// The writer's two pieces an <see cref="IAdRulesFlow"/> reuses rather than copies: <paramref name="Ask"/> posts one completion
/// (attempt number, the user prompt) under its own time budget and cleans the reply up, and <paramref name="RecordCaught"/> writes
/// the ring entry of a reply a rule flagged (its raw text) before the re-ask fires. The last attempt's entry is the writer's own.
/// </summary>
public sealed record AdRulesCalls(
    Func<int, string, Task<AdRulesReply>> Ask,
    Action<string> RecordCaught);
