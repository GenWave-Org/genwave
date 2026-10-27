using GenWave.VoiceOff.Deck;

namespace GenWave.VoiceOff.Render;

/// <summary><c>render --dry-run</c> (STORY-495): what a run would render and roughly how long it takes. Makes no request, asks docker nothing and writes no file.</summary>
internal static class DryRunReport
{
    internal static async Task<int> RunAsync(string root, IReadOnlyList<CardPlan> plans, TextWriter output, CancellationToken ct)
    {
        int paragraphs = plans.Sum(p => p.Paragraphs.Count);
        string cards = plans.Count == 1 ? "1 card" : $"{plans.Count} cards";
        output.WriteLine($"Dry run: {(paragraphs == 1 ? "1 paragraph" : $"{paragraphs} paragraphs")} across {cards}");
        foreach (CardPlan plan in plans)
        {
            output.WriteLine(plan.Refusal is { } refusal
                ? $"{plan.Card.Id}: ❌ {refusal}"
                : $"{plan.Card.Id}: {plan.Card.Engine} on {CardHostName.Of(plan.Card.Host)}, {plan.Paragraphs.Count} paragraphs");
        }

        output.WriteLine((await PreviousRunSpeed.FindAsync(root, ct)).EtaLine(paragraphs));
        return RenderCommand.Ok;
    }
}
