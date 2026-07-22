namespace GenWave.Core.Abstractions;

/// <summary>
/// The outcome of one <see cref="ILlmBatchGate.Evaluate"/> call (SPEC F85.3).
/// </summary>
/// <param name="Allowed">True when an offline batch may attempt the configured LLM right now.</param>
/// <param name="Reason">
/// Human-readable cause — always populated, whether allowed or not, so a caller can log it
/// unconditionally without a null check. When <paramref name="Allowed"/> is false, this is the
/// single line a caller logs once for the whole skipped batch, never once per item.
/// </param>
public sealed record LlmBatchGateDecision(bool Allowed, string Reason);
