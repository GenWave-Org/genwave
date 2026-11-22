namespace GenWave.Core.TalkRules;

/// <summary>A track the talk is about. <paramref name="Crossing"/> is the track being crossed into or out of, if any.</summary>
public sealed record TalkTrack(string Title, string Artist, string? Album, int? Year, TalkTrack? Crossing);
