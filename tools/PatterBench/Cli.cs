namespace GenWave.PatterBench;

/// <summary>The bench's entry point. Streams, the HTTP handler, the clock and the persona folder are injected so tests run it in-process.</summary>
internal static class Cli
{
    internal const int Ok = 0;
    internal const int Unreachable = 1;
    internal const int UsageError = 2;

    internal static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        HttpMessageHandler? handler = null,
        TimeProvider? clock = null,
        string? personaDirectory = null,
        CancellationToken ct = default)
    {
        clock ??= TimeProvider.System;
        var started = clock.GetUtcNow();
        if (!BenchArgs.TryParse(args, started, out var parsed, out var problem) || parsed is null)
        {
            stderr.WriteLine($"PatterBench: {problem}");
            stderr.WriteLine(BenchArgs.Usage);
            return UsageError;
        }

        var cards = new List<(string Slug, GenWave.Core.Domain.PersonaCard Card)>();
        foreach (var slug in parsed.Personas)
        {
            var card = await PersonaFixtures.LoadAsync(personaDirectory ?? PersonaFixtures.DefaultDirectory, slug, ct);
            if (card is null)
            {
                stderr.WriteLine($"PatterBench: unknown persona '{slug}'.");
                stderr.WriteLine(BenchArgs.Usage);
                return UsageError;
            }

            cards.Add((slug, card));
        }

        using var http = new HttpClient(handler ?? new SocketsHttpHandler(), disposeHandler: handler is null)
        {
            Timeout = TimeSpan.FromMinutes(5),
        };

        IReadOnlyList<BenchCell> cells;
        try
        {
            cells = await new BenchRunner(http, parsed, clock).RunAsync(cards, ct);
        }
        catch (EndpointUnreachableException ex)
        {
            stderr.WriteLine($"PatterBench: {ex.Message}");
            return Unreachable;
        }

        ReportFile.WriteOnce(parsed.OutPath, ReportWriter.Build(parsed, started, cells));
        stdout.WriteLine($"PatterBench: wrote {parsed.OutPath}");
        return Ok;
    }
}
