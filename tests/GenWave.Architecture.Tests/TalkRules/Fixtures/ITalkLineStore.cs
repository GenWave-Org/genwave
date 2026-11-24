namespace GenWave.Architecture.Tests.TalkRules.Fixtures;

/// <summary>Fixture collaborator: a store that a good shaper may take (HistoryAwareShaper) and a pure judge must not (JudgeTakingAStore, law R2).</summary>
public interface ITalkLineStore
{
    IReadOnlyList<string> LinesFor(long personaId);
}
