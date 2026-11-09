namespace GenWave.PatterBench;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using CancellationTokenSource cancel = new();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        return await Cli.RunAsync(args, Console.Out, Console.Error, ct: cancel.Token);
    }
}
