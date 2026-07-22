// Review finding on PLAN T68 — parity guard between the backend's LegacyPersonaCardMapper.Slugify
// (SPEC F71.1) and the admin UI's independently authored client mirror,
// admin-ui/app/(authed)/personas/persona-slug.ts's personaSlug. Undetected drift here silently
// 404s GET /api/personas/{slug}/export or lands POST /api/personas/{slug}/import on the WRONG
// persona row (upsert-by-slug).
//
// admin-ui/__specs__/persona-slug.parity-cases.ts is the ONE authored case table (each row a
// [name, expectedSlug] pair); admin-ui/__specs__/persona-slug.spec.ts asserts the real personaSlug
// against it. This fact is the C# half: it string-parses that SAME .ts file (the
// Story151/FeatureSettingsHelpKeysParity repo-content-fact idiom — no TS toolchain runs inside
// xUnit) and asserts the real LegacyPersonaCardMapper.Slugify against the same rows. It lives here
// (GenWave.MediaLibrary.Tests), not Host.Tests, because Slugify is internal and only this test
// project carries the InternalsVisibleTo grant from GenWave.MediaLibrary. A change to EITHER
// implementation that stops matching a row fails a spec on THAT toolchain, never a silent
// one-sided drift.

using System.Text.RegularExpressions;
using GenWave.MediaLibrary.Station;

namespace GenWave.MediaLibrary.Tests.Specs;

public static partial class FeaturePersonaSlugParity
{
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string PersonaSlugParityCasesTsPath =>
        Path.Combine(RepoRoot, "admin-ui", "__specs__", "persona-slug.parity-cases.ts");

    /// <summary>
    /// Extracts every <c>["name", "expectedSlug"]</c> row inside the
    /// <c>PERSONA_SLUG_PARITY_CASES</c> array literal only — bounded to that one array so a quoted
    /// pair inside a doc comment elsewhere in the file can never leak into the parsed case list.
    /// </summary>
    static IReadOnlyList<(string Name, string ExpectedSlug)> ParseTsParityCases()
    {
        var text = File.ReadAllText(PersonaSlugParityCasesTsPath);

        const string startMarker =
            "PERSONA_SLUG_PARITY_CASES: ReadonlyArray<readonly [string, string]> = [";
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"could not find '{startMarker}' in {PersonaSlugParityCasesTsPath}");
        var arrayBodyStart = start + startMarker.Length;

        var end = text.IndexOf("] as const", arrayBodyStart, StringComparison.Ordinal);
        Assert.True(end >= 0, $"could not find the closing '] as const' in {PersonaSlugParityCasesTsPath}");

        var arrayBody = text[arrayBodyStart..end];
        var rows = RowPattern().Matches(arrayBody)
            .Select(m => (Name: m.Groups[1].Value, ExpectedSlug: m.Groups[2].Value))
            .ToList();
        Assert.True(rows.Count > 0, $"parsed zero rows out of {PersonaSlugParityCasesTsPath}");
        return rows;
    }

    [GeneratedRegex("\\[\\s*\"([^\"]*)\"\\s*,\\s*\"([^\"]*)\"\\s*\\]")]
    private static partial Regex RowPattern();

    public static IEnumerable<object[]> ParityCases() =>
        ParseTsParityCases().Select(c => new object[] { c.Name, c.ExpectedSlug });

    public sealed class ScenarioTsCasesMatchTheRealSlugify
    {
        [Theory]
        [MemberData(nameof(ParityCases), MemberType = typeof(FeaturePersonaSlugParity))]
        public void SlugifyMatchesTheTsMirrorForEveryParityCase(string name, string expectedSlug)
        {
            Assert.Equal(expectedSlug, LegacyPersonaCardMapper.Slugify(name));
        }
    }
}
