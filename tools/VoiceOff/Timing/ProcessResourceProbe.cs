using GenWave.VoiceOff.Processes;

namespace GenWave.VoiceOff.Timing;

/// <summary>Probes with <c>docker stats --no-stream</c> and <c>nvidia-smi</c> (argv, no shell). Read-only: it never starts, stops or reconfigures a container.</summary>
internal sealed class ProcessResourceProbe(string dockerProgram = "docker", string nvidiaSmiProgram = "nvidia-smi") : IResourceProbe
{
    private static readonly string[] NvidiaSmiArguments = ["--query-gpu=memory.used", "--format=csv,noheader,nounits"];

    public Task<string?> ReadContainerMemoryAsync(string container, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);
        return RunAsync(dockerProgram, Arguments(container), ct);
    }

    public Task<string?> ReadGpuMemoryAsync(CancellationToken ct) => RunAsync(nvidiaSmiProgram, NvidiaSmiArguments, ct);

    /// <summary>The exact argv after <c>docker</c>; the name follows <c>--</c> so it can never be a flag.</summary>
    internal static IReadOnlyList<string> Arguments(string container) =>
        ["stats", "--no-stream", "--format", "{{.MemUsage}}", "--", container];

    private static async Task<string?> RunAsync(string program, IEnumerable<string> arguments, CancellationToken ct)
    {
        // A missing program (no nvidia-smi on a CPU host) or a non-zero exit is "no answer", not a failure.
        (int ExitCode, string Stdout, string Stderr)? result = await CliRunner.RunAsync(program, arguments, ct);
        return result is { ExitCode: 0 } ok ? ok.Stdout : null;
    }
}
