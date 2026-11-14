namespace GenWave.Tts;

public static partial class CopyClaims
{
    /// <summary>
    /// SPEC F218.15 — whether the accepted <paramref name="copy"/> actually read <paramref name="fact"/> on air:
    /// at least one claim the gate would check in the copy (a digit-run token or a weather-condition word,
    /// outside <paramref name="stationName"/>'s own spans) is supported by the fact, by the SAME rules
    /// <see cref="CheckFacts"/> applies (<see cref="IsDigitTokenSupported"/>, whole-word condition match, the
    /// station-name exemption). On a lane where <see cref="CheckFacts"/> runs, "the gate matched it" and "the copy
    /// used it" agree. On the lanes where it does not run (LeadIn/BackAnnounce) this predicate is the only judge,
    /// so it also exempts the track's own title and artist (<paramref name="trackTitle"/>,
    /// <paramref name="trackArtist"/>): a song called "Purple Rain" is not the DJ reading "rain" off the forecast.
    /// A fact with no claim of either class can never be matched, which errs toward not remembering a line
    /// nobody can prove was read.
    /// </summary>
    public static bool UsesFact(
        string copy, string fact, string? stationName = null, string? trackTitle = null, string? trackArtist = null)
    {
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(fact);

        var nameSpans = FindTitleSpans(copy, stationName);
        nameSpans.AddRange(FindTitleSpans(copy, trackTitle));
        nameSpans.AddRange(FindTitleSpans(copy, trackArtist));
        var factDigitTokens = DigitRunRx().Matches(fact).Select(match => match.Value).ToArray();

        return DistinctNonExemptTokens(DigitRunRx().Matches(copy), nameSpans)
                .Any(token => IsDigitTokenSupported(token, factDigitTokens))
            || DistinctNonExemptTokens(ConditionWordRx().Matches(copy), nameSpans)
                .Any(token => ContainsWord(fact, token));
    }
}
