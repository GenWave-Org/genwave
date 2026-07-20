namespace GenWave.Core.Domain;

/// <summary>
/// A <see cref="PersonaCard"/>'s voice profile (SPEC F71.1). <see cref="Engine"/> selects among
/// GenWave's TTS engine roles (F70.1, e.g. <c>"kokoro"</c>/<c>"piper"</c>); <c>""</c> defers to the
/// station's own default engine, mirroring <see cref="Persona.Voice"/>'s established empty-string
/// sentinel (SPEC F35.1) rather than inventing a second "unset" convention. <see cref="Pace"/> is a
/// speaking-rate multiplier (<c>1.0</c> = engine default).
/// </summary>
public sealed record VoiceSpec(
    string Engine,
    string VoiceId,
    double Pace,
    string Language);
