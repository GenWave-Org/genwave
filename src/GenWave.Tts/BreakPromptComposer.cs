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
        var personaSection = inputs.Shaping is not null
            ? LlmPromptBuilder.BuildShapedPersonaSection(inputs.Persona, inputs.Card, inputs.MentionOwnName)
            : LlmPromptBuilder.BuildPersonaSection(
                inputs.Persona, inputs.Card, inputs.MentionOwnName, inputs.Quirks, inputs.RecentLines, inputs.Taglines,
                inputs.Memory);
        var system = LlmPromptBuilder.BuildSystemPrompt(
            personaSection,
            inputs.MaxCopyChars,
            inputs.StationLocalNow,
            inputs.SoundTags,
            inputs.Shaping?.ForThisBreak(),
            inputs.Shaping is null ? null : LlmPromptBuilder.HasPersonaSection(inputs.Persona, inputs.Card));

        var clockLine = LlmPromptBuilder.BuildStationClockLine(inputs.StationLocalNow);
        var user = inputs.AnnouncementMessage is { } message
            ? LlmPromptBuilder.BuildAnnouncementUserContent(inputs.Request, clockLine, message)
            : LlmPromptBuilder.BuildUserContent(
                inputs.Request, clockLine, inputs.PreviouslyVoicedTasteNotes, inputs.PatterFact, inputs.ShowFlavor,
                inputs.Shaping is null ? inputs.Callback : null);

        return new BreakPrompt(system, user);
    }
}
