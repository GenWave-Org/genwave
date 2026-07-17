namespace GenWave.Core.Abstractions;

/// <summary>
/// Low-level TTS contract: text + voice in, an audio file path out (§0.1).
/// Faults surface as exceptions; the caller (<see cref="ITtsSegmentSource"/>) is responsible for
/// translating failures to null so that nothing propagates to the playout loop.
/// </summary>
public interface ITtsSynthesizer
{
    /// <summary>
    /// Synthesizes <paramref name="text"/> using the specified <paramref name="voice"/> and returns
    /// the absolute path to the generated audio file.
    /// </summary>
    Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct);
}
