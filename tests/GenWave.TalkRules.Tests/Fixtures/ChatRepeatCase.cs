namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>One chat reply (HOST then NEIGHBOR, alternating) and what each side said before, newest first.</summary>
/// <param name="HostTagline">The host's card tagline, when the card has one.</param>
internal sealed record ChatRepeatCase(
    IReadOnlyList<string> Lines,
    IReadOnlyList<RecentSaid> HostRecent,
    IReadOnlyList<RecentSaid> NeighborRecent,
    string HostTagline = "");
