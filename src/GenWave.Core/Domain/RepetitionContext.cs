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
/// <param name="CrossingTrackTitle">Title of the track the break crosses from (the one before); same.</param>
/// <param name="CrossingTrackArtist">Artist of the crossing track; same.</param>
/// <param name="Year">Current track year; its digits, and its number words when paired, never make a repeat on their own.</param>
/// <param name="AllowedPhrases">Taglines in quota this break (F218.33): a use of one of these is never a hit; a shared run touching it is cut at the use, and only a part outside it of MinRunWords or more words (still a repeat run) hits. A use is the key-run rule (F218.39): min(n, max(3, ⌈n/2⌉)) or more contiguous words of an n-word tagline, whole or partial, on normalised words.</param>
/// <param name="UnavailablePhrases">Taglines over quota or cooling down (F218.33): copy that uses one (same key-run rule) is a hit named by the phrase as written, even if its earlier uses fall outside <paramref name="RecentTexts"/>; a shared run touching it is cut at the tagline: the part outside it stays its own hit if it is still a repeat run (MinRunWords or more words), else it is folded into the tagline's hit.</param>
public sealed record RepetitionContext(
    IReadOnlyList<string> RecentTexts,
    IReadOnlyList<string> SpentQuirkRules,
    string? PersonaName = null,
    string? StationName = null,
    string? TrackTitle = null,
    string? Artist = null,
    string? Album = null,
    int? Year = null,
    string? CrossingTrackTitle = null,
    string? CrossingTrackArtist = null,
    IReadOnlyList<string>? AllowedPhrases = null,
    IReadOnlyList<string>? UnavailablePhrases = null);
