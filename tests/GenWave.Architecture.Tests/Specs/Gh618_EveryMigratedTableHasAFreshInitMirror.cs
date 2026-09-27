// gh-#618 — every table a db/NN migration creates also has a fresh-init CREATE in db/01 or db/06

using System.Text.RegularExpressions;
using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

/// <summary>
/// Only db/01-library.sh and db/06-station-settings-migration.sh run as Postgres init scripts, so a
/// table created ONLY by a later migration is missing from any init-scripts-only boot (the
/// MediaLibrary test fixture, a fresh compose start before migrate.sh). The per-column
/// InitialSchema facts (Story305/Story357) only catch mirrors someone remembered to pin; this law
/// sweeps every migration, so the next dropped table mirror turns red on its own.
/// </summary>
public static partial class FeatureEveryMigratedTableHasAFreshInitMirror
{
    // Migration-private tables no app code reads: the mirror rule has nothing to protect.
    static readonly IReadOnlyDictionary<string, string> Exempt = new Dictionary<string, string>
    {
        ["library.one_time_fix"] = "db/34's own one-time-fix ledger; only migration scripts read it",
    };

    [GeneratedRegex(@"create\s+table\s+(?:if\s+not\s+exists\s+)?([a-z_]+\.[a-z_]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CreateTableRx();

    static IEnumerable<string> TablesCreatedBy(string path) =>
        CreateTableRx().Matches(File.ReadAllText(path)).Select(m => m.Groups[1].Value.ToLowerInvariant());

    public sealed class ScenarioTheMirrorSweep
    {
        [Fact]
        public void EveryMigrationTableIsCreatedByAFreshInitScript()
        {
            var db = Path.Combine(SolutionLocator.Root(), "db");
            var freshInit = TablesCreatedBy(Path.Combine(db, "01-library.sh"))
                .Concat(TablesCreatedBy(Path.Combine(db, "06-station-settings-migration.sh")))
                .ToHashSet(StringComparer.Ordinal);

            var migrations = Directory.GetFiles(db, "*-migration.sh")
                .Where(p => Path.GetFileName(p) != "06-station-settings-migration.sh");
            var unmirrored = migrations
                .SelectMany(p => TablesCreatedBy(p).Select(t => (Table: t, File: Path.GetFileName(p))))
                .Where(x => !freshInit.Contains(x.Table) && !Exempt.ContainsKey(x.Table))
                .Select(x => $"{x.Table} ({x.File})")
                .Distinct()
                .ToList();

            Assert.True(unmirrored.Count == 0,
                "Tables with no db/01 or db/06 fresh-init CREATE: " + string.Join(", ", unmirrored));
        }

        [Fact]
        public void TheSweepSeesTheMigrations()
        {
            // Guards the law against going vacuous (a renamed db/ dir or a regex that stops matching).
            var db = Path.Combine(SolutionLocator.Root(), "db");
            Assert.Contains("station.schedule_special",
                TablesCreatedBy(Path.Combine(db, "36-schedule-special-migration.sh")));
        }

        [Fact]
        public void EveryExemptionStillNamesARealMigrationTable()
        {
            var db = Path.Combine(SolutionLocator.Root(), "db");
            var created = Directory.GetFiles(db, "*-migration.sh").SelectMany(TablesCreatedBy).ToHashSet();
            Assert.All(Exempt.Keys, t => Assert.Contains(t, created));
        }
    }
}
