using System.ComponentModel;
using System.Diagnostics;

namespace GenWave.VoiceOff.Speech;

/// <summary>Asks the docker CLI about a container with <c>docker inspect</c> only (argv, no shell).</summary>
internal sealed class DockerCliInspector : IDockerInspector
{
    private const string Format = "{{.State.Running}} {{range .NetworkSettings.Networks}}{{.IPAddress}} {{end}}";

    public async Task<ContainerState> InspectAsync(string container, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);
        ProcessStartInfo psi = new("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in Arguments(container))
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using Process process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start docker.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);
            Task<string> stderr = process.StandardError.ReadToEndAsync(ct);
            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            return process.ExitCode == 0
                ? Parse(await stdout)
                : Classify((await stderr).Trim());
        }
        catch (Win32Exception ex)
        {
            return ContainerState.DockerUnavailable($"docker is not available: {ex.Message}");
        }
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
