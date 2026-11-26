using GenWave.Tts;

namespace GenWave.TalkRules;

/// <summary>
/// Today's roll: <see cref="Random.Shared"/> against <see cref="LlmPromptBuilder.SelfNameMentionProbability"/>, the source and the odds
/// <c>LlmCopyWriter</c> uses for the legacy name line.
/// </summary>
public sealed class SelfNameRoll : ISelfNameRoll
{
    public bool Rolls() => Random.Shared.NextDouble() < LlmPromptBuilder.SelfNameMentionProbability;
}
