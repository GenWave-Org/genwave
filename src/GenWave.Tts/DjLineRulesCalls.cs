namespace GenWave.Tts;

/// <summary>One completion call's reply: the raw text and what the fixed clean-up made of it.</summary>
internal sealed record DjLineReply(string Raw, LlmCopyCleanupResult Cleanup);

/// <summary>
/// The writer's two pieces <see cref="DjLineRulesFlow"/> reuses rather than copies: <paramref name="Ask"/> posts one
/// completion (attempt number, user prompt) and cleans the reply, and <paramref name="Record"/> writes the call's ring entry
/// (the reply, and whether a rule flagged it).
/// </summary>
internal sealed record DjLineRulesCalls(
    Func<int, string, Task<DjLineReply>> Ask,
    Action<DjLineReply, bool> Record);
