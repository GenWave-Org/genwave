using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Render;

/// <summary>What <c>voiceoff render</c> was asked to do. <paramref name="Engines"/> empty means every profile. <paramref name="DryRun"/> only reports the plan and touches nothing.</summary>
internal sealed record RenderOptions(
    EngineTier Tier,
    IReadOnlyList<string> Engines,
    string Root,
    string EnginesDirectory,
    string GauntletDirectory,
    bool DryRun = false);
