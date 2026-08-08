using System.Text.RegularExpressions;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// STORY-293's doc-side half of the suite↔doc parity test: extracts every law id CONTRIBUTING.md's
/// laws table declares.
///
/// <b>The extraction convention (decided here — nothing upstream dictates it).</b> A law id counts
/// only when it is the FIRST cell of a markdown table row, written backtick-quoted — e.g.
/// <c>| `L1` | ... | ... |</c>. Anchoring on "first cell, backtick-quoted, row start" is what tells a
/// real table row apart from a prose mention of the same text (this very doc comment names several
/// law ids without a single one of them counting). The matched cell additionally has to look like a
/// law id — <see cref="LawId.IdPattern"/>, the SAME shape definition <see cref="LawId.All"/>'s own
/// const-filter consumes (STORY-293 review: one shape, owned by <see cref="LawId"/>, not a second
/// hand-rolled copy here that could quietly drift out of sync) — so a table whose first column is
/// backtick-quoted for some unrelated reason (a type name, say) is never mistaken for a law row.
/// </summary>
internal static class ContributingLawTable
{
    private static readonly Regex FirstCellPattern = new(@"^\|\s*`([^`]+)`\s*\|", RegexOptions.Compiled);
    private static readonly Regex LawIdShape = new(LawId.IdPattern, RegexOptions.Compiled);

    /// <summary>Every law id found in a table row's first cell, in document order, duplicates
    /// included — a genuine duplicate row is itself a doc defect for the caller to notice, not
    /// something this extractor silently collapses.</summary>
    public static IReadOnlyList<string> ExtractLawIds(string markdown) =>
        markdown
            .Split('\n')
            .Select(line => FirstCellPattern.Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Where(id => LawIdShape.IsMatch(id))
            .ToList();
}
