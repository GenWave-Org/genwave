using GenWave.VoiceOff.Script;

namespace GenWave.VoiceOff.Speech;

/// <summary>Everything the tool knows about one voice engine; read from <c>engines/&lt;slug&gt;.json</c> (SPEC F214.16).</summary>
internal sealed record EngineProfile(
    string Slug,
    bool Baseline,
    string Container,
    int Port,
    Uri? Endpoint,
    IReadOnlyList<EngineTier> Tiers,
    IReadOnlyDictionary<GauntletRegister, string> Voices,
    string VoiceReason,
    EngineRepick? Repick,
    IReadOnlyDictionary<string, string?> Markup,
    double Speed,
    int TimeoutSeconds,
    string License,
    string? Notes)
{
    /// <summary>The voice a register is rendered with: the re-pick when one names the register, else the first choice.</summary>
    internal string VoiceFor(GauntletRegister register) =>
        Repick is not null && Repick.Register == register ? Repick.Voice : Voices[register];
}
