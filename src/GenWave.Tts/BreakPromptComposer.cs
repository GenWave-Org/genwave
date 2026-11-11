namespace GenWave.Tts;

/// <summary>
/// The one place a break's system and user prompts are composed from <see cref="LlmPromptBuilder"/>
/// (SPEC F218.27). <see cref="LlmCopyWriter"/> composes on air through it, and tools/PatterBench
/// through the same call, so the bench's prompt cannot drift from what airs.
/// </summary>
internal static class BreakPromptComposer
{
    public static BreakPrompt Compose(BreakPromptInputs inputs)
    {
        var system = LlmPromptBuilder.BuildSystemPrompt(
            LlmPromptBuilder.BuildPersonaSection(
                inputs.Persona, inputs.Card, inputs.MentionOwnName, inputs.Quirks, inputs.RecentLines, inputs.Taglines),
            inputs.MaxCopyChars,
            inputs.StationLocalNow);

        var clockLine = LlmPromptBuilder.BuildStationClockLine(inputs.StationLocalNow);
        var user = inputs.AnnouncementMessage is { } message
            ? LlmPromptBuilder.BuildAnnouncementUserContent(inputs.Request, clockLine, message)
            : LlmPromptBuilder.BuildUserContent(
                inputs.Request, clockLine, inputs.PreviouslyVoicedTasteNotes, inputs.PatterFact, inputs.ShowFlavor);

        return new BreakPrompt(system, user);
    }
}
