using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace GenWave.VoiceOff.Serving;

/// <summary>
/// <c>voiceoff serve</c> (SPEC F214.10): serves a paired run's listening page until cancelled. Exit 0 on a clean stop, 2 for a usage error,
/// 1 for a run that is missing or not paired, or a port that cannot be used.
/// </summary>
internal static class ServeCommand
{
    internal const int Ok = 0;
    internal const int Failed = 1;
    internal const int UsageError = 2;

    /// <param name="args">The arguments after the <c>serve</c> verb.</param>
    internal static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter output, TextWriter error, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (!ServeOptionsParser.TryParse(args, out ServeOptions? options, out string? usage))
        {
            error.WriteLine(usage);
            return UsageError;
        }

        try
        {
            await using WebApplication app = ServeHost.Build(options, TimeProvider.System, output);
            await app.StartAsync(ct);
            await app.WaitForShutdownAsync(ct);
            await app.StopAsync(CancellationToken.None);
            return Ok;
        }
        catch (OperationCanceledException)
        {
            return Ok;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine(ex.Message);
            return Failed;
        }
    }
}
