using GenWave.VoiceOff.Render;

namespace GenWave.VoiceOff;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using CancellationTokenSource cancel = new();
        Console.CancelKeyPress += (_, e) =>
        {
            // Stop cleanly: the run finishes its bookkeeping and exits 130.
            e.Cancel = true;
            cancel.Cancel();
        };
        return await Cli.RunAsync(args, Console.Out, Console.Error, RenderServices.Default(), cancel.Token);
    }
}
