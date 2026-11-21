namespace GenWave.Tts;

/// <summary>
/// Per-engine record of speech-render activity, fed by <see cref="SpeechEngineClient"/> and read by
/// <see cref="VoiceEngineWarmUp"/> (SPEC F219.25, PLAN T768a). A single-threaded engine cannot answer a reachability
/// ping while it renders, so a render in flight or just finished is better evidence than the ping. Keyed by
/// <see cref="VoiceEngine.Identity"/>, so a switch to another engine and back never reuses old evidence for the wrong
/// instance. Thread-safe; every time comes from the injected <see cref="TimeProvider"/>.
/// <para>
/// One bad line is a grace miss, like one missed ping: the tile only goes red after <see cref="RedAfterFailures"/> failed
/// or timed-out renders in a row, and any success in between resets the count (Dean's T768a ruling).
/// </para>
/// </summary>
public sealed class EngineRenderActivity(TimeProvider? timeProvider = null)
{
    /// <summary>Failed or timed-out renders in a row (no success between) that turn the tile red; the ping rule's two misses.</summary>
    public const int RedAfterFailures = 2;

    readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    readonly Lock gate = new();
    readonly Dictionary<string, State> engines = new(StringComparer.Ordinal);

    public void Started(string identity)
    {
        lock (gate)
            StateFor(identity).InFlight++;
    }

    /// <summary>The render completed: it proves the engine works, so any earlier failure is forgotten.</summary>
    public void Succeeded(string identity)
    {
        lock (gate)
        {
            var state = StateFor(identity);
            state.InFlight = Math.Max(0, state.InFlight - 1);
            state.LastSucceededAt = time.GetUtcNow();
            state.FailuresInARow = 0;
            state.Failure = null;
        }
    }

    /// <summary>The render failed or ran past the engine's time limit.</summary>
    public void Failed(string identity, string reason)
    {
        lock (gate)
        {
            var state = StateFor(identity);
            state.InFlight = Math.Max(0, state.InFlight - 1);
            state.FailuresInARow++;
            state.Failure = reason;
        }
    }

    /// <summary>
    /// The render ended with no verdict on the engine: the caller walked away, the line was a bad request (4xx), or it was a
    /// preview. Neither proof of health nor of failure; the render is no longer in flight.
    /// </summary>
    public void Abandoned(string identity)
    {
        lock (gate)
        {
            var state = StateFor(identity);
            state.InFlight = Math.Max(0, state.InFlight - 1);
        }
    }

    public EngineRenderEvidence Read(string identity)
    {
        lock (gate)
            return engines.TryGetValue(identity, out var state)
                ? new EngineRenderEvidence(state.InFlight > 0, state.LastSucceededAt, state.FailuresInARow, state.Failure)
                : EngineRenderEvidence.None;
    }

    /// <summary>Consumes the reported failures, so they turn the tile red once and the next warm-up starts clean.</summary>
    public void ClearFailure(string identity)
    {
        lock (gate)
        {
            if (engines.TryGetValue(identity, out var state))
            {
                state.FailuresInARow = 0;
                state.Failure = null;
            }
        }
    }

    State StateFor(string identity)
    {
        if (!engines.TryGetValue(identity, out var state))
            engines[identity] = state = new State();
        return state;
    }

    sealed class State
    {
        public int InFlight;
        public DateTimeOffset? LastSucceededAt;
        public int FailuresInARow;
        public string? Failure;
    }
}
