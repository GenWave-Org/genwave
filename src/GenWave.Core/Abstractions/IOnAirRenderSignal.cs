namespace GenWave.Core.Abstractions;

/// <summary>
/// The read-only half of <c>GenWave.Host.Playout.OnAirRenderGate</c> (SPEC F161.1; STORY-391; PLAN
/// T402), exposed as a Core seam so a framework-free inner project can gate on the same real
/// in-flight signal <c>CrosstalkStockWorker</c> already reads directly (that class lives in
/// <c>GenWave.Host</c>, so it references <c>OnAirRenderGate</c> by its concrete type — GenWave.Host
/// depends on every inner project, never the reverse).
///
/// <para>
/// <b>The layering this seam exists to keep honest.</b> <c>GenWave.Ads</c>' own <c>AdSpotWorker</c>
/// needs the SAME "is an on-air render running right now" fact <c>OnAirRenderGate.InFlight</c>
/// already answers, but <c>GenWave.Ads</c> must never reference <c>GenWave.Host</c> (L5/L10 — Host is
/// the outermost layer; an inner project referencing it would cycle the dependency graph the moment
/// Host, in turn, registers <c>AdSpotWorker</c> as a hosted service). This interface is the injected
/// abstraction that closes that gap without new coupling: <c>OnAirRenderGate</c> implements it
/// directly (a Host type may freely implement a Core interface), and <c>GenWave.Host</c>'s own
/// composition root maps the two together — see <c>PlayoutServiceCollectionExtensions</c>' own
/// registration, right beside <c>OnAirRenderGate</c>'s own <c>AddSingleton</c> call.
/// </para>
///
/// <para>
/// <b>Read-only by design.</b> Only <c>PlayoutFeederService</c> ever calls
/// <c>OnAirRenderGate.Enter</c>/<c>Exit</c> — every other consumer (this seam's own callers included)
/// only ever needs to ASK whether a render is in flight, never to declare one. Narrowing the
/// published contract to <see cref="InFlight"/> alone keeps <c>AdSpotWorker</c> from ever being able
/// to mutate a signal it does not own.
/// </para>
/// </summary>
public interface IOnAirRenderSignal
{
    /// <summary>Whether an on-air LLM+TTS render is in flight right now (the SAME instant
    /// <c>OnAirRenderGate.InFlight</c> reports) — a reader observing a write a few nanoseconds late
    /// costs nothing worse than one extra worker tick's discard, mirroring that type's own
    /// remarks.</summary>
    bool InFlight { get; }
}
