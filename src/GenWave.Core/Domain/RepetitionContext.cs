namespace GenWave.Core.Domain;

/// <summary>
/// Everything <see cref="Abstractions.IRepetitionCheck"/> compares a candidate against (SPEC F218.11).
/// Null or empty names mean "no exclusion".
/// </summary>
/// <param name="RecentTexts">The persona's last utterances (aired or not, <c>fallback=false</c>).</param>
/// <param name="SpentQuirkRules">Rule texts of the <c>maxPerShift</c> quirks already spent this shift.</param>
/// <param name="PersonaName">Persona name; its words never make a repeat on their own.</param>
/// <param name="StationName">Station name; same.</param>
/// <param name="TrackTitle">Current track title; same.</param>
/// <param name="Artist">Current track artist; same.</param>
/// <param name="Album">Current track album; same.</param>
/// <param name="Year">Current track year; its digits, and its number words when paired, never make a repeat on their own.</param>
public sealed record RepetitionContext(
    IReadOnlyList<string> RecentTexts,
    IReadOnlyList<string> SpentQuirkRules,
    string? PersonaName = null,
    string? StationName = null,
    string? TrackTitle = null,
    string? Artist = null,
    string? Album = null,
    int? Year = null);
