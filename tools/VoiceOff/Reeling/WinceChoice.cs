namespace GenWave.VoiceOff.Reeling;

/// <summary>The engines that get a wince reel, in no meaningful order, and whether the set is short of the usual three.</summary>
internal sealed record WinceChoice(IReadOnlyList<string> Engines, bool IsShort);
