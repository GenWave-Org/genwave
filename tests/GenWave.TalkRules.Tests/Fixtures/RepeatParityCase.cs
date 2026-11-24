namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>One DJ-line reply and what the speaker said before, newest first. Nothing else is set, so only the repeat check can trip.</summary>
/// <param name="Copy">The cleaned reply.</param>
/// <param name="Recent">The speaker's earlier lines, newest first; more than ten are allowed.</param>
/// <param name="Tagline">The speaker's card tagline, when the card has one.</param>
/// <param name="TrackTitle">The current track's title, when there is one; its words never make a repeat.</param>
/// <param name="TrackArtist">The current track's artist.</param>
/// <param name="TrackAlbum">The current track's album; its words never make a repeat.</param>
/// <param name="TrackYear">The current track's release year; saying it never makes a repeat.</param>
/// <param name="CrossingTitle">The title of the track crossing the handoff, when there is one.</param>
/// <param name="CrossingArtist">The crossing track's artist.</param>
/// <param name="PersonaName">The speaker's name; saying it never makes a repeat.</param>
/// <param name="StationName">The station's name; saying it never makes a repeat.</param>
internal sealed record RepeatParityCase(
    string Copy,
    IReadOnlyList<RecentSaid> Recent,
    string Tagline = "",
    string? TrackTitle = null,
    string? TrackArtist = null,
    string? TrackAlbum = null,
    int? TrackYear = null,
    string? CrossingTitle = null,
    string? CrossingArtist = null,
    string PersonaName = "Flip",
    string StationName = "GenWave");
