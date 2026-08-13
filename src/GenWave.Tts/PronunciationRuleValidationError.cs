namespace GenWave.Tts;

/// <summary>One field-named reason a candidate pronunciation rule fails
/// <see cref="PronunciationRuleValidator.Validate"/> (SPEC F97.1, F97.5; T144's rules API).
/// <see cref="Field"/> names the JSON property (<c>pattern</c>/<c>word</c>/<c>ipa</c>) an
/// operator-facing 400 should attribute the message to — mirrors
/// <c>GenWave.Host.Configuration.SettingValidator</c>'s own per-key
/// <c>ValidationProblemDetails.Errors</c> shape, one layer down.</summary>
public sealed record PronunciationRuleValidationError(string Field, string Message);
