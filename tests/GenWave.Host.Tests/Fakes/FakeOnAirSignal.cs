using GenWave.Core.Abstractions;

namespace GenWave.Host.Tests.Fakes;

/// <summary>A settable <see cref="IOnAirRenderSignal"/>.</summary>
sealed class FakeOnAirSignal : IOnAirRenderSignal
{
    public bool InFlight { get; set; }
}
