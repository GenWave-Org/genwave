// STORY-521 — no word of the box he deleted (F216 · PLAN T650)
//
// BDD specification — xUnit. The brief's acceptance grep, run from inside the suite: over the
// named paths, case-insensitive, no hit for any of the seven words. CHANGELOG.md, docs/ and
// .github/workflows/ are exempt (brief). All four facts run since T650.

using System.Text.RegularExpressions;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureNoWordOfTheBoxHeDeleted
{
    static string Root => RepoRootLocator.Find(AppContext.BaseDirectory);

    // Assembled at runtime so this file never matches itself.
    static readonly string[] Words =
    [
        "cloud" + "flared",
        "cad" + "dy",
        "PUBLIC_" + "HOST",
        "TUNNEL_" + "TOKEN",
        "compose\\." + "demo",
        "appli" + "ance",
        "topology \\(" + "a\\)",
    ];

    static readonly Regex Pattern = new(string.Join("|", Words), RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly string[] SkippedSegments = ["bin", "obj", "node_modules", ".next", "TestResults"];

    static IEnumerable<string> RootFiles() =>
        Directory.EnumerateFiles(Root, "compose*.yaml", SearchOption.TopDirectoryOnly)
            .Concat(new[] { ".env.example", "launch.sh", "setup.sh", "migrate.sh", Path.Combine("observability", "LABELS.md") }
                .Select(f => Path.Combine(Root, f)));

    static IEnumerable<string> TreeFiles(string dir) =>
        Directory.EnumerateFiles(Path.Combine(Root, dir), "*", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(seg => SkippedSegments.Contains(seg, StringComparer.Ordinal)));

    static string[] Hits(IEnumerable<string> files) =>
        files.Where(File.Exists)
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, line, i)))
            .Where(t => Pattern.IsMatch(t.line))
            .Select(t => $"{Path.GetRelativePath(Root, t.f)}:{t.i + 1}")
            .ToArray();

    public static class ScenarioTheGrepIsEmpty
    {
        [Fact]
        public static void Root_scripts_and_compose_files_are_clean()
        {
            Assert.Empty(Hits(RootFiles()));
        }

        [Fact]
        public static void Tools_is_clean()
        {
            Assert.Empty(Hits(TreeFiles("tools")));
        }

        [Fact]
        public static void Src_is_clean()
        {
            Assert.Empty(Hits(TreeFiles("src")));
        }

        [Fact]
        public static void Tests_are_clean()
        {
            Assert.Empty(Hits(TreeFiles("tests")));
        }
    }
}
