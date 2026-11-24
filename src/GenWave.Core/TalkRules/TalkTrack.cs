namespace GenWave.Core.TalkRules;

/// <summary>
/// A track the talk is about. <paramref name="Title"/> is null when only a crossing track is known (no current track).
/// <paramref name="Crossing"/> is the track being crossed into or out of, if any.
/// </summary>
public sealed record TalkTrack(string? Title, string Artist, string? Album, int? Year, TalkTrack? Crossing);
