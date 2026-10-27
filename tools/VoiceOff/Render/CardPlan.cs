using GenWave.VoiceOff.Deck;
using GenWave.VoiceOff.Text;

namespace GenWave.VoiceOff.Render;

/// <summary>What one card will send to its engine, or why it must not (<see cref="Refusal"/>). Decided before any container or request is touched.</summary>
internal sealed record CardPlan(Card Card, IReadOnlyList<ParagraphText> Paragraphs, string? Refusal)
{
    internal bool Refused => Refusal is not null;
}
