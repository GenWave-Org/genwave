using GenWave.VoiceOff.Deck;
using GenWave.VoiceOff.Text;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>The real script and card builders for the same-text-in specs; no IO beyond reading the repo's files.</summary>
internal static class SameTextScenario
{
    private static string ToolDirectory => Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "tools", "VoiceOff");

    internal static string CardsDirectory => Path.Combine(ToolDirectory, "cards");

    internal static ScriptFile Script() => ScriptFile.Load(Path.Combine(ToolDirectory, "script", "script.json"));

    internal static Card ShippedCard(string id) =>
        CardReader.Parse(File.ReadAllText(Path.Combine(ToolDirectory, "cards", id + ".json")), id + ".json");

    /// <summary>A best-effort card with exactly these cue mappings and overrides.</summary>
    internal static Card BestEffort(
        string cues, IReadOnlyDictionary<int, string>? overrides = null, params string[] cannot)
    {
        Card card = CardReader.Parse(CardFixtureDirectory.CardJson("same-text", "kokoro", cues: cues), "same-text.json");
        return card with
        {
            Overrides = overrides ?? new Dictionary<int, string>(),
            Facts = card.Facts with { Cannot = cannot },
        };
    }

    internal static string Source(ScriptFile script, int paragraph) => script.Paragraphs[paragraph - 1].Text;

    internal static ParagraphText Prepared(Card card, ScriptFile script, int paragraph) =>
        CardText.Prepare(card, script)[paragraph - 1];
}
