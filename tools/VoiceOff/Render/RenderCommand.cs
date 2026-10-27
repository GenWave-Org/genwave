using GenWave.VoiceOff.Deck;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Text;

namespace GenWave.VoiceOff.Render;

/// <summary>
/// <c>voiceoff render</c> (SPEC F217.4 to F217.6): every card (or one) through its engine, a paragraph at a time, loudness-matched into a
/// run folder with <c>run.json</c> and <c>renders.jsonl</c>. A card that is refused, whose container is down, or whose paragraph fails is a row
/// in the output and in <c>run.json</c>, never a failed run.
/// </summary>
internal static class RenderCommand
{
    internal const int Ok = 0;
    internal const int Failed = 1;
    internal const int UsageError = 2;
    internal const int Interrupted = 130;

    /// <param name="args">The arguments after the <c>render</c> verb.</param>
    internal static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter output, TextWriter error, RenderServices services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(services);

        if (!RenderOptionsParser.TryParse(args, out RenderOptions? options, out string? usage) || options is null)
        {
            error.WriteLine(usage);
            return UsageError;
        }

        if (!TryLoad(options, error, out EngineProfileSet? engines, out CardSet? cards, out ScriptFile? script) || engines is null || cards is null || script is null)
        {
            return Failed;
        }

        IReadOnlyList<Card> selected = cards.Cards.Where(c => options.CardId is null || string.Equals(c.Id, options.CardId, StringComparison.Ordinal)).ToList();
        if (selected.Count == 0)
        {
            error.WriteLine($"Unknown card '{options.CardId}'. Known cards: {string.Join(", ", cards.Cards.Select(c => c.Id))}.");
            return UsageError;
        }

        IReadOnlyList<CardPlan> plans = selected.Select(c => CardPlanner.Plan(c, script)).ToList();
        if (options.DryRun)
        {
            return await DryRunReport.RunAsync(options.Root, plans, output, ct);
        }

        try
        {
            return await RunPlannedAsync(options, engines, script, plans, output, error, services, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            output.WriteLine("Cancelled; the files written so far are kept.");
            return Interrupted;
        }
    }

    private static bool TryLoad(RenderOptions options, TextWriter error, out EngineProfileSet? engines, out CardSet? cards, out ScriptFile? script)
    {
        engines = null;
        cards = null;
        script = null;
        try
        {
            engines = EngineProfileSet.Load(options.EnginesDirectory);
            cards = CardSet.Load(options.CardsDirectory, engines);
            script = ScriptFile.Load(options.ScriptPath);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or DirectoryNotFoundException)
        {
            error.WriteLine(ex.Message);
            return false;
        }
    }

    private static async Task<int> RunPlannedAsync(
        RenderOptions options, EngineProfileSet engines, ScriptFile script, IReadOnlyList<CardPlan> plans,
        TextWriter output, TextWriter error, RenderServices services, CancellationToken ct)
    {
        RunLayout layout = new(options.Root, options.RunId);
        (RunManifest? manifest, string? problem) = await RunOpening.OpenAsync(layout, script.Hash, options.CardId is not null, services.Time.GetUtcNow(), ct);
        if (manifest is null)
        {
            error.WriteLine(problem);
            return Failed;
        }

        output.WriteLine($"Run {layout.RunId} -> {layout.RunDirectory}");
        RunProgress progress = new(plans.Sum(p => p.Paragraphs.Count));
        try
        {
            await new CardRunner(services, engines, layout, new RenderLog(layout.RendersPath), progress, output).RunAsync(plans, manifest, ct);
            return Ok;
        }
        catch (InvalidDataException ex)
        {
            // A renders.jsonl with a bad line: nothing of the card has been touched yet.
            error.WriteLine(ex.Message);
            return Failed;
        }
    }
}
