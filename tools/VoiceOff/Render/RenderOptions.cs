namespace GenWave.VoiceOff.Render;

/// <summary>
/// What <c>voiceoff render</c> was asked to do (SPEC F217.5). <paramref name="CardId"/> null means every card;
/// a card id rewrites that one card in the run. <paramref name="DryRun"/> only reports the plan and touches nothing.
/// </summary>
internal sealed record RenderOptions(
    string RunId,
    string? CardId,
    string Root,
    string EnginesDirectory,
    string CardsDirectory,
    string ScriptPath,
    bool DryRun = false);
