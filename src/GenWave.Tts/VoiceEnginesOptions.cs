namespace GenWave.Tts;

/// <summary>
/// Binds <c>Tts:Engines:&lt;name&gt;</c> (SPEC F219.1). Bound from the <c>Tts</c> section, so
/// <see cref="Engines"/> is the <c>Engines</c> child. Legacy <c>Tts:Endpoint</c> /
/// <c>Tts:PiperPrimaryEndpoint</c> seed <c>kokoro</c> / <c>piper</c> in post-configure when no entry exists.
/// </summary>
public sealed class VoiceEnginesOptions
{
    public const string Section = TtsOptions.Section;

    public Dictionary<string, VoiceEngineEntry> Engines { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
