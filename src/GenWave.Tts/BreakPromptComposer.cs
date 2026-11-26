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
        var shaped = inputs.Shaping is not null;
        var personaSection = shaped
            ? LlmPromptBuilder.BuildShapedPersonaSection(inputs.Persona, inputs.Card)
            : LlmPromptBuilder.BuildPersonaSection(
                inputs.Persona, inputs.Card, inputs.MentionOwnName, inputs.Quirks, inputs.RecentLines, inputs.Taglines,
                inputs.Memory);
        var system = LlmPromptBuilder.BuildSystemPrompt(
            personaSection,
            inputs.MaxCopyChars,
            shaped ? null : inputs.StationLocalNow, // the clock guard line rides under "For this break" in rules form
            inputs.SoundTags,
            inputs.Shaping?.ForThisBreak(),
            shaped ? LlmPromptBuilder.HasPersonaSection(inputs.Persona, inputs.Card) : null);

        var clockLine = LlmPromptBuilder.BuildStationClockLine(inputs.StationLocalNow);
        var user = inputs.AnnouncementMessage is { } message
            ? LlmPromptBuilder.BuildAnnouncementUserContent(inputs.Request, clockLine, message)
            : LlmPromptBuilder.BuildUserContent(
                inputs.Request, clockLine, shaped ? [] : inputs.PreviouslyVoicedTasteNotes, inputs.PatterFact,
                shaped ? null : inputs.ShowFlavor, shaped ? null : inputs.Callback);

        return new BreakPrompt(system, user);
    }
}
