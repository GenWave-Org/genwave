namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// STORY-293 AC2/AC3's diff: pure set comparison between the suite's law ids
/// (<see cref="LawId.All"/>) and CONTRIBUTING.md's table (<see cref="ContributingLawTable"/>),
/// exercised both by the live fact (real ids, real file) and by <c>ScenarioDriftIsRed</c>'s hermetic
/// probes (synthetic id lists, no file I/O) — the same function either way, so the probe genuinely
/// proves what the live fact relies on rather than testing a lookalike.
/// </summary>
internal static class LawParity
{
    public static LawParityResult Compare(IEnumerable<string> suiteIds, IEnumerable<string> docIds)
    {
        var suite = new HashSet<string>(suiteIds, StringComparer.Ordinal);
        var doc = new HashSet<string>(docIds, StringComparer.Ordinal);

        return new LawParityResult(
            suite.Except(doc).OrderBy(id => id, StringComparer.Ordinal).ToList(),
            doc.Except(suite).OrderBy(id => id, StringComparer.Ordinal).ToList());
    }

    /// <summary>Names every offending id on both sides (STORY-293 AC3: "fails naming the missing law
    /// id") — never a bare "parity failed" a reader would have to go diffing by hand to explain.</summary>
    public static string Format(LawParityResult result)
    {
        var parts = new List<string>();
        if (result.MissingFromDoc.Count > 0)
            parts.Add($"missing from CONTRIBUTING.md's laws table: {string.Join(", ", result.MissingFromDoc)}");
        if (result.ExtraInDoc.Count > 0)
        {
            parts.Add(
                "present in CONTRIBUTING.md's laws table but not a real suite law id: "
                + string.Join(", ", result.ExtraInDoc));
        }

        return string.Join("; ", parts);
    }
}
