namespace GenWave.Memory;

/// <summary>
/// The claims a new claim is compared against, pre-processed once (content-word stems and given names) so
/// <see cref="LifeClaimRules.Reject"/> does not re-tokenise every known row for every claim. Grows as claims
/// from one reply are accepted.
/// </summary>
public sealed class LifeKnownClaims
{
    readonly List<IReadOnlySet<string>> stemSets = [];
    readonly HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="claims">Known claims, forgotten included.</param>
    /// <param name="rosterNames">Full names of the roster personas; their words are never given names.</param>
    public LifeKnownClaims(IEnumerable<string> claims, IEnumerable<string> rosterNames)
    {
        RosterWords = rosterNames
            .SelectMany(n => RepetitionText.Tokens(n))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var claim in claims)
            Add(claim);
    }

    internal IReadOnlyList<IReadOnlySet<string>> StemSets => stemSets;

    internal IReadOnlySet<string> RosterWords { get; }

    internal IReadOnlySet<string> Names => names;

    public void Add(string claim)
    {
        stemSets.Add(LifeClaimRules.ContentStems(claim));
        names.UnionWith(LifeClaimRules.GivenNames(claim, RosterWords));
    }
}
