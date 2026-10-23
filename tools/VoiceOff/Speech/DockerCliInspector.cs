using GenWave.VoiceOff.Processes;

namespace GenWave.VoiceOff.Speech;

/// <summary>Asks the docker CLI about a container with <c>docker inspect</c> only (argv, no shell).</summary>
internal sealed class DockerCliInspector(string program = "docker") : IDockerInspector
{
    private const string Format = "{{.State.Running}} {{range .NetworkSettings.Networks}}{{.IPAddress}} {{end}}";

    public async Task<ContainerState> InspectAsync(string container, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);
        (int ExitCode, string Stdout, string Stderr)? result = await CliRunner.RunAsync(program, Arguments(container), ct);
        return result switch
        {
            null => ContainerState.DockerUnavailable("docker is not available: the program was not found"),
            { ExitCode: 0 } ok => Parse(ok.Stdout),
            { } failed => Classify(failed.Stderr.Trim()),
        };
    }

    /// <summary>The exact argv after <c>docker</c>: read-only <c>inspect</c>, the name after <c>--</c> so it can never be a flag.</summary>
    internal static IReadOnlyList<string> Arguments(string container) =>
        ["inspect", "--type", "container", "--format", Format, "--", container];

    /// <summary>A failed <c>docker inspect</c>: "No such container" is a missing container; anything else means docker could not answer.</summary>
    internal static ContainerState Classify(string stderr) =>
        stderr.Contains("No such container", StringComparison.Ordinal)
            ? ContainerState.Missing(stderr)
            : ContainerState.DockerUnavailable(stderr);

    internal static ContainerState Parse(string output)
    {
        string[] parts = output.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool running = parts.Length > 0 && string.Equals(parts[0], "true", StringComparison.Ordinal);
        string? ip = parts.Length > 1 ? parts[1] : null;
        return new ContainerState(true, running, ip);
    }
}
