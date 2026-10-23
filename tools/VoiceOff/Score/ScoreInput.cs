using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Score;

/// <summary>What <see cref="ResultsMarkdown"/> is a pure function of. <paramref name="BudgetSeconds"/> adds the "your box" speed line when given.</summary>
internal sealed record ScoreInput(EngineProfileSet Profiles, IReadOnlyList<TierRun> Runs, int? BudgetSeconds);
