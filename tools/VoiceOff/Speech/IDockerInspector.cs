namespace GenWave.VoiceOff.Speech;

/// <summary>Read-only view of a docker container. The tool never starts, stops or creates one.</summary>
internal interface IDockerInspector
{
    Task<ContainerState> InspectAsync(string container, CancellationToken ct);
}
