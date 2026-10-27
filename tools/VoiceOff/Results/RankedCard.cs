using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Serving;

namespace GenWave.VoiceOff.Score;

/// <summary>One row of the ranking: <paramref name="Rank"/> 1 is the verdict. <paramref name="Judged"/> is null for a card the judge never touched.</summary>
internal sealed record RankedCard(int Rank, string CardId, CardEntry Entry, CardJudgement? Judged);
