namespace GenWave.VoiceOff.Score;

/// <summary>What <c>voiceoff score</c> was asked to do (SPEC F217.9).</summary>
internal sealed record ScoreOptions(string RunId, string Root, string EnginesDirectory, string CardsDirectory, string ScriptPath, string? OutPath);
