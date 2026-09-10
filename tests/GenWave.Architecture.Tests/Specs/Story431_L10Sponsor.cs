// STORY-431 — L10 knows station.sponsor (SPEC F176.3 · PLAN T452)

using System.Text.RegularExpressions;
using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

/// <summary>
/// STORY-431 AC5 and SPEC F176.3 both say "L10's root list gains station.sponsor" — but L10's
/// real root list (<see cref="FeatureNamespaceCycleFreedom.ProjectRoots"/>) enumerates NAMESPACES,
/// so a table name added there would be a vacuous pin that asserts a string sits in a list the
/// check never reads — the same reasoning STORY-405 already worked out for db/45's three pack
/// tables (<see cref="FeatureL10RootListKnowsThePackTables"/>). This class pins what AC5 is
/// actually reaching for instead: <c>station.sponsor</c> genuinely exists with the shape and FKs
/// db/46 built, on the station_svc side of the db/22 role boundary, mirrored verbatim in db/06's
/// fresh-init file, and the namespace-cycle law's own root list already covers every project this
/// epic touched.
/// </summary>
public static class FeatureL10RootListKnowsTheSponsorTable
{
    static string ReadDb46Text()
        => File.ReadAllText(Path.Combine(SolutionLocator.Root(), "db", "46-sponsor-migration.sh"));

    static string ReadDb06Text()
        => File.ReadAllText(Path.Combine(SolutionLocator.Root(), "db", "06-station-settings-migration.sh"));

    static string CollapseWhitespace(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    static int CountOccurrences(string haystack, string needle)
        => Regex.Matches(haystack, Regex.Escape(needle)).Count;

    /// <summary>Slices the <c>CREATE TABLE IF NOT EXISTS station.sponsor (</c> ... <c>);</c> block
    /// out of a migration file's text. Safe on both db/46 and db/06 because the block runs from
    /// that opening line to the first <c>);</c> that follows it — no <c>)</c> inside the block
    /// (the ones closing a column's own <c>CHECK (...)</c> or the trailing <c>CONSTRAINT ... (...)</c>
    /// clause) is ever immediately followed by <c>;</c>, so that first <c>);</c> is always the
    /// table's own close.</summary>
    static string ExtractSponsorTableBlock(string text)
    {
        const string start = "CREATE TABLE IF NOT EXISTS station.sponsor (";
        var startIndex = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, "station.sponsor's CREATE TABLE was not found.");

        var endIndex = text.IndexOf(");", startIndex, StringComparison.Ordinal);
        Assert.True(endIndex >= 0, "station.sponsor's CREATE TABLE has no closing ');'.");

        return text[startIndex..(endIndex + 2)];
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheSponsorTableLivesInTheStationSchema
    {
        [Fact]
        public void Db46CreatesStationSponsor()
            => Assert.Contains(
                "CREATE TABLE IF NOT EXISTS station.sponsor (",
                ReadDb46Text(),
                StringComparison.Ordinal);

        [Fact]
        public void Db46DeclaresTheUniqueNullsNotDistinctKey()
            => Assert.Contains(
                "CONSTRAINT sponsor_pack_slug_name_key UNIQUE NULLS NOT DISTINCT (pack_slug, name_key)",
                ReadDb46Text(),
                StringComparison.Ordinal);

        [Fact]
        public void Db46AddsTheThreeRestrictFks()
        {
            // Whitespace-insensitive: each constraint name and its FOREIGN KEY clause sit on
            // separate, differently-indented lines in the source (ADD CONSTRAINT on one line,
            // FOREIGN KEY on the next), so the raw text is collapsed to single spaces first.
            var text = CollapseWhitespace(ReadDb46Text());
            const string referencesClause =
                "FOREIGN KEY (sponsor_id) REFERENCES station.sponsor(id) ON DELETE RESTRICT";

            foreach (var constraintName in new[]
            {
                "ad_brief_sponsor_id_fkey",
                "ad_spot_sponsor_id_fkey",
                "show_sponsor_id_fkey",
            })
            {
                Assert.Contains($"{constraintName} {referencesClause}", text, StringComparison.Ordinal);
            }

            Assert.Equal(3, CountOccurrences(text, referencesClause));
        }

        [Fact]
        public void Db06MirrorsDb46sSponsorTableVerbatim()
        {
            // Hand-synced SQL mirrors need a text-equality pin, not an eyeballed comparison (PLAN
            // T452 ruling; the gh-#618 lesson every migration in this directory follows) —
            // db/06 carries its own independently-maintained copy of the same CREATE TABLE for a
            // fresh install, and nothing forces an edit to one file to reach the other. The two
            // blocks are byte-identical today, including indentation: same tabs, same column
            // spacing, same trailing comment. A deliberate divergence (re-indenting one file, or
            // changing one file's copy without the other) must edit both db/46 and db/06 and
            // this pin together — the pin does not tolerate whitespace-only drift.
            var db46Block = ExtractSponsorTableBlock(ReadDb46Text());
            var db06Block = ExtractSponsorTableBlock(ReadDb06Text());

            Assert.Equal(db46Block, db06Block, StringComparer.Ordinal);
        }
    }

    public sealed class ScenarioTheSponsorTableFollowsTheDb22RoleBoundary
    {
        [Fact]
        public void TheSponsorTableIsCreatedWhileActingAsStationSvcNeverLibrarySvc()
        {
            // db/45's role-boundary fact (FeatureL10RootListKnowsThePackTables) pins its three
            // tables between a `set role station_svc;` switch and a later `set role library_svc;`
            // switch. db/46 never performs that second switch at all (grepped, not assumed) — the
            // whole migration body runs as station_svc from its one `set role` line to its own
            // `COMMIT;`, so this fact pins the boundary db/46 actually has: after station_svc,
            // before COMMIT.
            var text = ReadDb46Text();

            var stationRoleIndex = text.IndexOf("set role station_svc;", StringComparison.Ordinal);
            Assert.True(stationRoleIndex >= 0, "db/46 no longer sets role station_svc before the sponsor table.");
            Assert.DoesNotContain("set role library_svc;", text, StringComparison.Ordinal);

            var commitIndex = text.IndexOf("COMMIT;", stationRoleIndex, StringComparison.Ordinal);
            Assert.True(commitIndex > stationRoleIndex, "db/46 has no COMMIT after its station_svc switch.");

            var createIndex = text.IndexOf("CREATE TABLE IF NOT EXISTS station.sponsor (", StringComparison.Ordinal);
            Assert.InRange(createIndex, stationRoleIndex, commitIndex);
        }
    }

    public sealed class ScenarioTheNamespaceCycleLawStillCoversEveryTouchedProject
    {
        [Fact]
        public void NoL1ToL10ViolationAppears()
        {
            // Replaces the original "the L1-L10 suite runs green after the pin" claim: that claim
            // is already covered by every other law's own theory data — the same substitution
            // STORY-405's own L10SweepsEveryProjectTheEpicTouched fact made for the jingle/voice
            // pack epic. What AC5 actually needs pinned is narrower — the namespace-cycle law's
            // own root list still names every project this epic touched, so a future edit that
            // quietly drops one of them from FeatureNamespaceCycleFreedom.ProjectRoots goes red
            // here.
            IEnumerable<string> roots = FeatureNamespaceCycleFreedom.ProjectRoots;

            foreach (var projectTheEpicTouched in new[]
            {
                "GenWave.Core",
                "GenWave.MediaLibrary",
                "GenWave.Host",
                "GenWave.Ads",
                "GenWave.Tts",
                "GenWave.Orchestration",
            })
            {
                Assert.Contains(projectTheEpicTouched, roots);
            }
        }
    }
}
