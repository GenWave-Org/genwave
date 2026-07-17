namespace GenWave.Core.Abstractions;

/// <summary>
/// Lists the voice ids installed on the configured TTS backend (SPEC F29.4). Mirrors
/// <see cref="ITtsSynthesizer"/>'s shape: faults surface as exceptions — the caller
/// (the voices endpoint) is responsible for the cache-then-502 fallback.
/// </summary>
public interface ITtsVoiceLister
{
    /// <summary>
    /// Returns the voice ids the TTS backend currently has available.
    /// </summary>
    Task<IReadOnlyList<string>> ListVoicesAsync(CancellationToken ct);
}
