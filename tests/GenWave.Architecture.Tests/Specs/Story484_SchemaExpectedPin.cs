// STORY-484 — The schema journal (gh-#868 · SPEC F211.4 · PLAN T592)
//
// BDD specification — xUnit. AC6 (this file), AC11 (db/06 exempt) and the mirror pin are all live. Each Given comment
// names the arrange the scenario needs.

using System.Diagnostics;
using GenWave.Architecture.Tests.Support;
using GenWave.Core;

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureSchemaExpectedPin
{
    /// <summary>
    /// The station.schema_migration CREATE TABLE extraction/whitespace-normalisation used by
    /// <see cref="ScenarioSchemaMigrationDdlIsMirroredByteIdentically"/> (migrate.sh vs. db/06, both
    /// files in full) — one definition of "the same DDL, whitespace aside".
    /// </summary>
    static class SchemaMigrationDdl
    {
        internal const string TableMarker = "CREATE TABLE IF NOT EXISTS station.schema_migration";

        internal static string ExtractBlock(string source)
        {
            var start = source.IndexOf(TableMarker, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException($"could not find '{TableMarker}' to extract");

            var end = source.IndexOf(");", start, StringComparison.Ordinal);
            if (end < 0)
                throw new InvalidOperationException($"found '{TableMarker}' but no closing ');' after it");

            return source[start..(end + 2)];
        }

        internal static string Normalize(string block) =>
            string.Join(' ', block.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public sealed class ScenarioExpectedMatchesDb
    {
        // Given: the highest NN among db/NN-*-migration.sh in the repo

        /// <summary>AC6 — SchemaVersion.Expected equals it (a migration PR that forgets the bump is red)</summary>
        [Fact]
        public void ExpectedIsTheHighestMigration() => Assert.Equal(SchemaVersion.Expected, HighestMigrationNumber());

        /// <summary>
        /// The SAME "leading run of ASCII digits" parse rule
        /// <c>GenWave.MediaLibrary.Station.SchemaJournalRepository</c>'s own SQL applies to journalled
        /// <c>script</c> rows (that type's own remarks) — applied here to <c>db/</c>'s own file names
        /// instead. A name with no leading digit at all (never happens today: every real
        /// <c>db/*-migration.sh</c> starts with <c>NN-</c>) is ignored rather than thrown on, the same
        /// "an unparseable row doesn't participate" posture the SQL side takes; numeric comparison
        /// (never lexical) so <c>100</c> would outrank <c>99</c>.
        /// </summary>
        static int HighestMigrationNumber()
        {
            var dbDir = Path.Combine(SolutionLocator.Root(), "db");
            return Directory.GetFiles(dbDir, "*-migration.sh")
                .Select(Path.GetFileName)
                .OfType<string>()
                .Select(TryLeadingNumber)
                .OfType<int>()
                .Max();
        }

        static int? TryLeadingNumber(string fileName)
        {
            var digitCount = 0;
            while (digitCount < fileName.Length && char.IsAsciiDigit(fileName[digitCount]))
                digitCount++;

            return digitCount > 0 && int.TryParse(fileName[..digitCount], out var number) ? number : null;
        }
    }

    public sealed class ScenarioMigrationsAreNotEdited
    {
        // Given: `git diff --name-only --no-renames --diff-filter=a <merge-base> -- db/` on the branch

        /// <summary>
        /// AC11 — no existing migration script MODIFIED, RENAMED or DELETED (the journal lives in
        /// migrate.sh; a brand-new db/NN-*-migration.sh added on the branch is not an edit of an existing
        /// one). migrate.sh re-runs every db/*-migration.sh on every pass (the journal only records it), so an
        /// edit to an old script lands mid-sequence on every box, fresh and upgraded alike; keeping old
        /// scripts frozen keeps each one's behaviour fixed to what it did when it shipped, and puts every
        /// schema change in a new, numbered script. Uses <c>--no-renames --diff-filter=a</c>
        /// (lowercase — excludes only pure Additions), not <c>--diff-filter=M</c>: a rename with
        /// <c>--no-renames</c> shows as the old path deleted and the new path added, and the
        /// exclude-Additions filter still lets the deletion of the old path through, so a renamed-away or
        /// deleted existing script is caught the same as an edited one. Diffs against the merge-base with
        /// <c>origin/main</c>, not main's own moving tip, so a branch that's behind main never sees main's
        /// own later commits as "changed". db/06 is exempt entirely (Dean, 2026-11-08): it is the fresh-init
        /// mirror that test databases and fresh installs build from, and it must keep matching the newer
        /// db/NN scripts, which re-apply idempotently after it. Its schema_migration CREATE is still pinned equal to migrate.sh's by
        /// <see cref="ScenarioSchemaMigrationDdlIsMirroredByteIdentically"/>.
        /// </summary>
        [Fact]
        public void NoMigrationScriptChanged() => Assert.Empty(ChangedMigrationScripts.Value);

        const string FreshInitMirrorPath = "db/06-station-settings-migration.sh";

        static readonly Lazy<string[]> ChangedMigrationScripts = new(() =>
        {
            var repoRoot = SolutionLocator.Root();
            var mergeBase = ResolveMergeBase(repoRoot);

            var (exitCode, stdOut, stdErr) =
                RunGit(repoRoot, "diff", "--name-only", "--no-renames", "--diff-filter=a", mergeBase, "--", "db/");
            if (exitCode != 0)
                throw new InvalidOperationException($"git diff against {mergeBase} failed:\n{stdErr}");

            return stdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(path => path.EndsWith("-migration.sh", StringComparison.Ordinal))
                .Where(path => path != FreshInitMirrorPath)
                .ToArray();
        });

        /// <summary>
        /// Resolves the merge-base with origin/main, fetching just enough history to find one. A full
        /// local checkout already has it (no network touched). A shallow CI checkout
        /// (<c>actions/checkout@v7</c> defaults to <c>fetch-depth: 1</c>) may have neither origin/main
        /// locally nor enough history for HEAD and origin/main to share a visible ancestor — so this
        /// fetches main into an explicit local ref, then deepens both sides' history in bounded steps
        /// until a merge-base appears, rather than an unbounded <c>--unshallow</c> for a check that only
        /// ever needs the nearest common ancestor.
        /// </summary>
        static string ResolveMergeBase(string repoRoot)
        {
            if (TryMergeBase(repoRoot, out var mergeBase))
                return mergeBase;

            GitSucceeds(repoRoot, "fetch", "--depth=1", "origin", "main:refs/remotes/origin/main");
            var head = RunGit(repoRoot, "rev-parse", "HEAD").StdOut.Trim();

            for (var attempt = 0; attempt < 10; attempt++)
            {
                if (TryMergeBase(repoRoot, out mergeBase))
                    return mergeBase;

                var deepenedMain = GitSucceeds(repoRoot, "fetch", "--deepen=50", "origin", "main:refs/remotes/origin/main");
                var deepenedHead = GitSucceeds(repoRoot, "fetch", "--deepen=50", "origin", head);
                if (!deepenedMain && !deepenedHead)
                    break;
            }

            throw new InvalidOperationException(
                "could not resolve a merge-base between HEAD and origin/main, even after fetching origin/main " +
                "and deepening both sides' history — AC11 needs a real base ref to diff against.");
        }

        static bool TryMergeBase(string repoRoot, out string mergeBase)
        {
            if (!GitSucceeds(repoRoot, "rev-parse", "--verify", "-q", "origin/main"))
            {
                mergeBase = "";
                return false;
            }

            var (exitCode, stdOut, _) = RunGit(repoRoot, "merge-base", "HEAD", "origin/main");
            if (exitCode != 0)
            {
                mergeBase = "";
                return false;
            }

            mergeBase = stdOut.Trim();
            return true;
        }

        static bool GitSucceeds(string repoRoot, params string[] args) => RunGit(repoRoot, args).ExitCode == 0;

        static (int ExitCode, string StdOut, string StdErr) RunGit(string repoRoot, params string[] args)
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("failed to start git");
            var stdOut = process.StandardOutput.ReadToEnd();
            var stdErr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, stdOut, stdErr);
        }
    }

    public sealed class ScenarioSchemaMigrationDdlIsMirroredByteIdentically
    {
        // Given: the CREATE TABLE station.schema_migration block in migrate.sh and in db/06, whitespace-normalized

        static readonly Lazy<(string MigrateSh, string Db06)> Blocks = new(() =>
        {
            var repoRoot = SolutionLocator.Root();
            var migrateSh = SchemaMigrationDdl.Normalize(
                SchemaMigrationDdl.ExtractBlock(File.ReadAllText(Path.Combine(repoRoot, "migrate.sh"))));
            var db06 = SchemaMigrationDdl.Normalize(
                SchemaMigrationDdl.ExtractBlock(File.ReadAllText(Path.Combine(repoRoot, "db", "06-station-settings-migration.sh"))));
            return (migrateSh, db06);
        });

        /// <summary>Arrange sanity — a change to either file's marker/closing text would make extraction fail loudly instead of silently comparing empty strings.</summary>
        [Fact]
        public void BothExtractionsAreFound()
        {
            var (migrateSh, db06) = Blocks.Value;
            Assert.True(migrateSh.Length > 0 && db06.Length > 0,
                $"expected both extractions to be non-empty; migrate.sh={migrateSh.Length} chars, db/06={db06.Length} chars");
        }

        /// <summary>AC11 / gh-#618 — migrate.sh's preamble and db/06's fresh-init mirror declare the identical table.</summary>
        [Fact]
        public void MigrateShAndDb06MirrorTheSameTable()
        {
            var (migrateSh, db06) = Blocks.Value;
            Assert.Equal(migrateSh, db06);
        }
    }
}
