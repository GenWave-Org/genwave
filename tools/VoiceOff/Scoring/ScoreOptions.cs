namespace GenWave.VoiceOff.Scoring;

/// <summary>What <c>score</c> was asked for. <paramref name="Out"/> is null for the default <c>tools/VoiceOff/RESULTS.md</c>.</summary>
internal sealed record ScoreOptions(IReadOnlyList<string> Runs, string Root, string EnginesDirectory, string? Out, int? BudgetSeconds);
