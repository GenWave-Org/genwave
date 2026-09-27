// STORY-484 — The schema journal (gh-#868 · SPEC F211.4 · PLAN T592)
//
// BDD specification — xUnit. AC11 and the mirror pin are live (T591); the facts still marked
// [Fact(Skip = Pending)] go green in T592. Each Given comment names the arrange the scenario needs.

using System.Diagnostics;
using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureSchemaExpectedPin
{
    const string Pending = "pending: T592 — SchemaVersion.Expected pin (STORY-484)";

    /// <summary>
    /// The station.schema_migration CREATE TABLE extraction/whitespace-normalisation shared by
    /// <see cref="ScenarioSchemaMigrationDdlIsMirroredByteIdentically"/> (migrate.sh vs. db/06, both
    /// files in full) and <see cref="ScenarioMigrationsAreNotEdited"/> (migrate.sh's block vs. only the
    /// lines db/06's diff added) — one definition of "the same DDL, whitespace aside" for both checks.
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
        [Fact(Skip = Pending)]
        public void ExpectedIsTheHighestMigration() => Assert.Fail(Pending);
    }

    public sealed class ScenarioMigrationsAreNotEdited
    {
        // Given: `git diff --name-only --no-renames --diff-filter=a <merge-base> -- db/` on the branch

        /// <summary>
        /// AC11 — no existing migration script MODIFIED, RENAMED or DELETED (the journal lives in
        /// migrate.sh; a brand-new db/NN-*-migration.sh added on the branch is not an edit of an existing
        /// one). Uses <c>--no-renames --diff-filter=a</c> (lowercase — excludes only pure Additions), not
        /// <c>--diff-filter=M</c>: a rename with <c>--no-renames</c> shows as the old path deleted and the
        /// new path added, and the exclude-Additions filter still lets the deletion of the old path
        /// through, so a renamed-away or deleted existing script is caught the same as an edited one.
        /// Diffs against the merge-base with <c>origin/main</c>, not main's own moving tip, so a branch
        /// that's behind main never sees main's own later commits as "changed". db/06's own mirror
        /// addition (SPEC F211.3's one sanctioned exception — the fresh-init table, not a data-shape
        /// change to an existing migration) is excluded, but only when its diff is EXACTLY that: zero
        /// removed lines, and the added lines — blank lines and comment-only lines dropped, whitespace
        /// normalised — equal migrate.sh's own station.schema_migration CREATE TABLE block, character for
        /// character. Any removed line, any added line outside that block (a GRANT, a second statement,
        /// an unrelated edit), or added text that doesn't match migrate.sh's block exactly, fails db/06.
        /// </summary>
        [Fact]
        public void NoMigrationScriptChanged() => Assert.Empty(ChangedMigrationScripts.Value);

        const string SchemaMigrationMirrorPath = "db/06-station-settings-migration.sh";

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
                .Where(path => !(path == SchemaMigrationMirrorPath && IsOnlyTheSchemaMigrationMirrorAddition(repoRoot, mergeBase)))
                .ToArray();
        });

        /// <summary>
        /// True only when db/06's whole diff against <paramref name="mergeBase"/> is pure addition (zero
        /// removed lines) and the added lines — once blanks and comment-only (<c>--</c>) lines are
        /// dropped and whitespace is normalised the same way <see cref="SchemaMigrationDdl.Normalize"/>
        /// does — are EQUAL to migrate.sh's own normalised station.schema_migration CREATE TABLE block.
        /// A GRANT, a comment-on, or any other statement riding alongside the mirror shows up as extra
        /// added text the equality check doesn't expect, so it fails db/06 instead of waving it through.
        /// </summary>
        static bool IsOnlyTheSchemaMigrationMirrorAddition(string repoRoot, string mergeBase)
        {
            var (exitCode, stdOut, stdErr) =
                RunGit(repoRoot, "diff", "--unified=0", mergeBase, "--", SchemaMigrationMirrorPath);
            if (exitCode != 0)
                throw new InvalidOperationException($"git diff of {SchemaMigrationMirrorPath} against {mergeBase} failed:\n{stdErr}");

            var lines = stdOut.Split('\n');
            var removedLines = lines.Where(line => line.StartsWith('-') && !line.StartsWith("---", StringComparison.Ordinal)).ToArray();
            if (removedLines.Length > 0)
                return false;

            var addedContent = lines
                .Where(line => line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
                .Select(line => line[1..].Trim())
                .Where(line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal))
                .ToArray();
            if (addedContent.Length == 0)
                return false;

            var normalizedAdded = SchemaMigrationDdl.Normalize(string.Join(' ', addedContent));
            var expectedMirror = SchemaMigrationDdl.Normalize(
                SchemaMigrationDdl.ExtractBlock(File.ReadAllText(Path.Combine(repoRoot, "migrate.sh"))));

            return normalizedAdded == expectedMirror;
        }

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
