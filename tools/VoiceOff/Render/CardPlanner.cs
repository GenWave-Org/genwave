using GenWave.VoiceOff.Deck;
using GenWave.VoiceOff.Text;

namespace GenWave.VoiceOff.Render;

/// <summary>The pure preflight a card passes before it is rendered or dry-run (SPEC F217.4): its overrides name real paragraphs and its text keeps the script's words.</summary>
internal static class CardPlanner
{
    internal static CardPlan Plan(Card card, ScriptFile script)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(script);

        int[] missing = card.Overrides.Keys.Where(n => n > script.Paragraphs.Count).Order().ToArray();
        if (missing.Length > 0)
        {
            return Refuse(card, $"overrides paragraph {string.Join(", ", missing)}, but the script has {script.Paragraphs.Count} paragraphs");
        }

        IReadOnlyList<ParagraphText> paragraphs = CardText.Prepare(card, script);
        IReadOnlyList<WordsLawFailure> failures = WordsLaw.Check(card, paragraphs);
        return failures.Count == 0
            ? new CardPlan(card, paragraphs, null)
            : Refuse(card, "words law: " + string.Join(" ", failures.Select(f => f.Message)));
    }

    private static CardPlan Refuse(Card card, string reason) => new(card, [], reason);
}
