using GenWave.VoiceOff.Deck;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Serving;

namespace GenWave.VoiceOff.Score;

/// <summary>Everything <c>score</c> reads, already loaded: the run, its card files (by id), the judgement and the render log.</summary>
internal sealed record ScoreInput(
    RunManifest Manifest,
    IReadOnlyDictionary<string, Card> Cards,
    Judgement Judgement,
    IReadOnlyList<RenderRecord> Renders);
