using GenWave.VoiceOff.Text;

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
    IReadOnlyDictionary<string, string?> Markup,
    double Speed,
    int TimeoutSeconds,
    string License,
    string? Notes)
{
    /// <summary>The voice a register is rendered with: the profile's choice for it.</summary>
    internal string VoiceFor(GauntletRegister register) =>
        Voices[register];
}
