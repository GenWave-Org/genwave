namespace GenWave.VoiceOff.Speech;

/// <summary>
/// What <c>docker inspect</c> said about a container. <paramref name="Detail"/> explains a missing container
/// or a docker that could not answer; <paramref name="DockerAnswered"/> tells those two apart.
/// </summary>
internal sealed record ContainerState(bool Exists, bool Running, string? IpAddress, string? Detail = null, bool DockerAnswered = true)
{
    internal static ContainerState Missing(string detail) => new(false, false, null, detail);

    internal static ContainerState DockerUnavailable(string detail) => new(false, false, null, detail, DockerAnswered: false);
}
