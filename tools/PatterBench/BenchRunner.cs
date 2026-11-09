namespace GenWave.PatterBench;

using System.Text.Json;
using GenWave.Core.Domain;
using GenWave.Tts;

/// <summary>
/// Runs N breaks for each model and persona through the real prompt builder and truth gate. Every break
/// of a persona sees that persona's own earlier copies in the recent-lines block, as on air with memory on.
/// </summary>
internal sealed class BenchRunner(HttpClient http, BenchArgs args, TimeProvider clock)
{
    private readonly Dictionary<string, double?> vramByModel = new(StringComparer.Ordinal);

    internal async Task<IReadOnlyList<BenchCell>> RunAsync(
        IReadOnlyList<(string Slug, PersonaCard Card)> personas, CancellationToken ct)
    {
        var cells = new List<BenchCell>();
        foreach (var model in args.Models)
        {
            foreach (var (slug, card) in personas)
            {
                cells.Add(await RunCellAsync(model, slug, card, ct));
            }
        }

        return cells;
    }

    private async Task<BenchCell> RunCellAsync(
        string model, string slug, PersonaCard card, CancellationToken ct)
    {
        var random = new Random(BenchFixtures.Seed);
        var selector = new BenchQuirkSelector(random);
        var copies = new List<string>();
        var latencies = new List<TimeSpan>();
        var gatePasses = 0;
        var cfg = new LlmOptions { Endpoint = args.Endpoint.ToString(), Model = model };
        var requestUri = EndpointUri.Combine(cfg.Endpoint, "/v1/chat/completions");

        for (var i = 0; i < args.Breaks; i++)
        {
            var request = BenchFixtures.RequestFor(i, card.Name);
            var mentionOwnName = random.NextDouble() < LlmPromptBuilder.SelfNameMentionProbability;
            var quirks = await selector.SelectAsync(null, card, null, spend: true, ct);
            var recent = Enumerable.Reverse(copies).Take(MemoryBudget.Standard.RecentLines).ToList();
            var prompt = BreakPromptComposer.Compose(new BreakPromptInputs(
                request, null, card, mentionOwnName, quirks, recent, cfg.MaxCopyChars, BenchFixtures.LocalNow, []));

            var started = clock.GetTimestamp();
            var reply = await PostAsync(requestUri, cfg, prompt, ct);
            latencies.Add(clock.GetElapsedTime(started));
            if (!vramByModel.ContainsKey(model))
            {
                vramByModel[model] = await OllamaProcessList.VramGiBAsync(http, args.Endpoint, model, ct);
            }

            // The writer's own path: a reply hygiene rejects (empty, reasoning-only) has no text to air, so it
            // fails the gate and is neither scored nor remembered; an over-length one is scored as trimmed.
            if (LlmCopyWriter.TextOf(LlmCopyWriter.CleanupOf(reply, cfg.MaxCopyChars)) is not { } copy)
            {
                continue;
            }

            copies.Add(copy);
            var verdict = LlmCopyWriter.CheckTruthGate(
                copy, null, BenchFixtures.LocalNow, request.Track?.Title, request.StationName);
            if (verdict.Passed)
            {
                gatePasses++;
            }
        }

        return new BenchCell(model, slug, card, copies, gatePasses, latencies, vramByModel[model]);
    }

    private async Task<CompletionReply> PostAsync(Uri requestUri, LlmOptions cfg, BreakPrompt prompt, CancellationToken ct)
    {
        try
        {
            return await LlmCopyWriter.PostChatCompletionAsync(
                http, requestUri, cfg, prompt.System, prompt.User, LlmCopyWriter.DeriveMaxTokens(cfg.MaxCopyChars), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new EndpointUnreachableException(args.Endpoint.ToString(), ex.Message, ex);
        }
    }
}
