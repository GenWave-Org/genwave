using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Score;

/// <summary>One engine in one lane of one tier: the overall tally and each judge's own.</summary>
internal sealed record Cell(string Engine, RenderLane Lane, Tally Total, IReadOnlyDictionary<string, Tally> PerJudge);
