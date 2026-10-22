namespace GenWave.VoiceOff.Tests.Support;

/// <summary>How many pairs one judge decides for the challenger, for the baseline, or not at all.</summary>
internal sealed record JudgeTally(string Judge, int Wins, int Losses, int Ties = 0);
