// STORY-521 — no word of the box he deleted (F216 · PLAN T650)
//
// BDD specification — xUnit. The brief's acceptance grep, run from inside the suite: over the
// named paths, case-insensitive, no hit for any of the seven words. CHANGELOG.md, docs/ and
// .github/workflows/ are exempt (brief). All four facts run since T650.

using System.Diagnostics;
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

    // Repo files git doesn't ignore: tracked plus untracked-but-not-ignored (git ls-files from the repo
    // root), so a new un-added file is scanned while gitignored local output (TestResults, bin, obj,
    // node_modules, .next) can never turn this red. An empty or failed listing fails loudly.
    static readonly Lazy<string[]> RepoFiles = new(ListRepoFiles);

    static string[] ListRepoFiles()
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("ls-files");
        info.ArgumentList.Add("-z");
        info.ArgumentList.Add("--cached");
        info.ArgumentList.Add("--others");
        info.ArgumentList.Add("--exclude-standard");

        using var git = Process.Start(info)
            ?? throw new InvalidOperationException("git ls-files did not start.");
        var stdout = git.StandardOutput.ReadToEndAsync();
        var stderr = git.StandardError.ReadToEndAsync();
        git.WaitForExit();
        if (git.ExitCode != 0)
            throw new InvalidOperationException($"git ls-files failed ({git.ExitCode}): {stderr.Result}");

        var files = stdout.Result.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(files);
        return files;
    }

    static readonly string[] RootNames = [".env.example", "launch.sh", "setup.sh", "migrate.sh", "observability/LABELS.md"];

    // A scope that matches no files would pass vacuously, so each scope asserts it found some.
    static string[] Scope(IEnumerable<string> relativePaths)
    {
        var files = relativePaths.Select(f => Path.Combine(Root, f)).ToArray();
        Assert.NotEmpty(files);
        return files;
    }

    static string[] RootFiles() =>
        Scope(RepoFiles.Value
            .Where(f => RootNames.Contains(f, StringComparer.Ordinal)
                || (!f.Contains('/') && f.StartsWith("compose", StringComparison.Ordinal) && f.EndsWith(".yaml", StringComparison.Ordinal))));

    static string[] TreeFiles(string dir) =>
        Scope(RepoFiles.Value.Where(f => f.StartsWith(dir + "/", StringComparison.Ordinal)));

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
