using GenWave.Core.Abstractions;

namespace GenWave.Host.Playout;

/// <summary>
/// A tiny Host-side signal, true for the exact span <see cref="PlayoutFeederService"/> blocks
/// inside <c>PlayoutFeeder.RefillAsync</c> — the on-air LLM+TTS render window (gh-#184) — and read
/// by anything that must never compete with it for CPU on the same box
/// (<c>GenWave.Orchestration.CrosstalkBreakWindow</c>'s real-signal face, SPEC F127.7, PLAN T286
/// review F1).
///
/// <para>
/// <b>Bracketing <c>RefillAsync</c> itself, not re-reading <c>LlmCopyWriter</c>'s own SPEC F69.6
/// single-flight gate (build-time decision, PLAN T286 review).</b> That gate was the other
/// candidate: read-only occupancy (<c>SemaphoreSlim.CurrentCount == 0</c>) would have touched none
/// of <c>LlmCopyWriter</c>'s byte-sensitive request machinery (golden-pinned request bytes). It was
/// rejected anyway — it means something narrower than what SPEC F127.7 actually needs blocked:
/// <c>LlmCopyWriter</c>'s own remarks describe that gate as covering only "the request PLUS the
/// hygiene/salvage pass", releasing the instant the completion is cleaned, BEFORE the kokoro TTS
/// synth that follows still runs inside the SAME <c>RefillAsync</c> span
/// <see cref="PlayoutFeederService"/> actually blocks the tick for — so it would read idle for the
/// TTS-synth tail of a real on-air render, exactly the contention window gh-#277/kokoro pressure
/// already names. It would also read busy for the operator-preview path (<c>IPersonaPreviewWriter</c>
/// calls <c>LlmCopyWriter</c> directly, never through <c>RefillAsync</c>) — a completion that carries
/// no on-air render at all — over-blocking in the harmless direction, but still not what this signal
/// claims to mean. Bracketing <c>RefillAsync</c> itself is the one signal that means exactly what
/// SPEC F127.7 needs it to mean, at the cost of <see cref="PlayoutFeederService"/> owning one more
/// singleton dependency.
/// </para>
///
/// <para>
/// Deliberately not an <see cref="IDisposable"/> scope-guard (the house one-type-per-file rule
/// forbids the nested helper class that pattern would need) — <see cref="PlayoutFeederService"/>
/// calls <see cref="Enter"/>/<see cref="Exit"/> directly around its own <c>try</c>/<c>finally</c>.
/// A single <see langword="volatile"/> field is enough: there is exactly one writer (the single
/// station's feeder tick loop) and any number of readers, none of which need more than "is a render
/// in flight right now" — a reader observing a write a few nanoseconds late costs nothing worse than
/// one extra stock-timer tick's discard (SPEC F127.7's own "opportunistic, off the clock" framing).
/// </para>
///
/// <para>
/// <b>Also implements <see cref="IOnAirRenderSignal"/> (PLAN T402).</b> <c>GenWave.Ads</c>' own
/// <c>AdSpotWorker</c> needs this exact fact but must never reference <c>GenWave.Host</c> (L5/L10) —
/// see that interface's own remarks for the full layering rationale and where the two are mapped
/// together in DI.
/// </para>
/// </summary>
public sealed class OnAirRenderGate : IOnAirRenderSignal
{
    volatile bool inFlight;

    /// <summary>Whether an on-air render is in flight right now.</summary>
    public bool InFlight => inFlight;

    /// <summary>Marks an on-air render as started. Paired with <see cref="Exit"/> by the caller's
    /// own <c>try</c>/<c>finally</c> — never call this without one.</summary>
    public void Enter() => inFlight = true;

    /// <summary>Marks the on-air render that <see cref="Enter"/> started as finished.</summary>
    public void Exit() => inFlight = false;
}
