namespace GenWave.VoiceOff.Render;

/// <summary>One engine's state across a run's cards: the container gate is asked once, and the breaker counts cards in a row it did not answer. <see cref="Renderer"/> is null exactly when the gate failed, and <see cref="GateFailure"/> then says why.</summary>
internal sealed record EngineSession(CardRenderer? Renderer, string? GateFailure, DeadEngineBreaker Breaker)
{
    internal const string NoGateReason = "the engine did not pass its gate";

    /// <summary>A session whose gate failed; the reason is never blank, even when the gate gave none.</summary>
    internal static EngineSession Unavailable(string? reason) =>
        new(null, string.IsNullOrWhiteSpace(reason) ? NoGateReason : reason, new DeadEngineBreaker());
}
