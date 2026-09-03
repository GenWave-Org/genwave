using GenWave.Core.Abstractions;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// <see cref="IOnAirRenderSignal"/> double (PLAN T402) — a plain settable flag, the SAME shape
/// <c>OnAirRenderGate.Enter</c>/<c>Exit</c> flips in production, controllable directly from a spec
/// rather than through the real gate's own pairing discipline.
/// </summary>
public sealed class FakeOnAirRenderSignal : IOnAirRenderSignal
{
    public bool InFlight { get; set; }
}
