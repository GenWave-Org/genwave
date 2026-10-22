using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>Answers <c>docker inspect</c> from a fixed state; records the names it was asked about.</summary>
internal sealed class FakeDockerInspector(ContainerState state) : IDockerInspector
{
    internal List<string> Asked { get; } = [];

    public Task<ContainerState> InspectAsync(string container, CancellationToken ct)
    {
        Asked.Add(container);
        return Task.FromResult(state);
    }
}
