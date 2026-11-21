namespace GenWave.Tts.Tests.Fakes;

/// <summary>An <see cref="IActiveVoiceEngine"/> a spec can repoint mid-run, as a saved <c>Tts:Engine</c> switch does.</summary>
public sealed class SwitchableActiveEngine(VoiceEngine initial) : IActiveVoiceEngine
{
    public VoiceEngine? Current { get; set; } = initial;

    public string Name => Current?.Name ?? string.Empty;
}
