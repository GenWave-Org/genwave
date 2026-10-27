using System.Diagnostics.CodeAnalysis;
using GenWave.VoiceOff.Deck;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Serving;

/// <summary>
/// A rendered run opened for serving: the cards its <c>run.json</c> lists (the only cards the routes know), their definitions from the cards folder,
/// and the judgement store. A card file that has gone since the render leaves its card listed without details.
/// </summary>
internal sealed class ServeRun
{
    private readonly RunLayout layout;
    private readonly RunManifest manifest;
    private readonly IReadOnlyDictionary<string, Card> definitions;

    private ServeRun(RunLayout layout, RunManifest manifest, IReadOnlyDictionary<string, Card> definitions, JudgementStore judgements)
    {
        this.layout = layout;
        this.manifest = manifest;
        this.definitions = definitions;
        Judgements = judgements;
        CardIds = [.. manifest.Cards.Keys.Order(StringComparer.Ordinal)];
    }

    internal string RunId => layout.RunId;

    internal string RunDirectory => layout.RunDirectory;

    internal JudgementStore Judgements { get; }

    /// <summary>Every card of the run, in id order.</summary>
    internal IReadOnlyList<string> CardIds { get; }

    internal bool HasCard(string id) => manifest.Cards.ContainsKey(id);

    internal StateCard Describe(string id)
    {
        CardEntry entry = manifest.Cards[id];
        return new StateCard(id, entry.Host, entry.Rendered, entry.Reason, definitions.TryGetValue(id, out Card? card) ? StateCardDetails.From(card) : null);
    }

    /// <summary>The wav of a card that rendered, found by the run's own card ids only, never by a path from the request.</summary>
    internal bool TryGetClip(string id, [NotNullWhen(true)] out string? file)
    {
        file = null;
        if (!manifest.Cards.TryGetValue(id, out CardEntry? entry) || !entry.Rendered)
        {
            return false;
        }

        string path = layout.ClipPath(id);
        if (!File.Exists(path))
        {
            return false;
        }

        file = path;
        return true;
    }

    /// <exception cref="InvalidDataException">The run was not rendered, a file of it is bad, or a card file is bad.</exception>
    /// <exception cref="DirectoryNotFoundException">The engines or cards folder is missing.</exception>
    internal static async Task<ServeRun> OpenAsync(ServeOptions options, TimeProvider time, CancellationToken ct)
    {
        RunLayout layout = new(options.Root, options.RunId);
        if (!File.Exists(layout.RunJsonPath))
        {
            throw new InvalidDataException($"Run {options.RunId} has no run.json under {options.Root}; render it first: voiceoff render --run {options.RunId}");
        }

        RunManifest manifest = await RunManifest.ReadAsync(layout.RunJsonPath, ct);
        string? badId = manifest.Cards.Keys.FirstOrDefault(id => !CardReader.IsValidId(id));
        if (badId is not null)
        {
            throw new InvalidDataException($"run.json lists a card id that is not a card id: \"{badId}\".");
        }

        CardSet cards = CardSet.Load(options.CardsDirectory, EngineProfileSet.Load(options.EnginesDirectory));
        ServeRun run = new(
            layout,
            manifest,
            cards.Cards.ToDictionary(c => c.Id, StringComparer.Ordinal),
            new JudgementStore(Path.Combine(layout.RunDirectory, "judgement.json"), time));

        // A judgement file that does not parse stops serve here, not on the judge's first page load.
        _ = await run.Judgements.ReadAsync(ct);
        return run;
    }
}
