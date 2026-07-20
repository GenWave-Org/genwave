namespace GenWave.Tts;

/// <summary>
/// One operator-authored pronunciation correction: replace <see cref="From"/> with <see cref="To"/>
/// wherever it appears in booth-bound text. Compiled and matched by <see cref="SpeechCorrectionSet"/>;
/// this record only carries the raw operator data (SPEC F68.5).
/// </summary>
public sealed record SpeechCorrection(string From, string To);
