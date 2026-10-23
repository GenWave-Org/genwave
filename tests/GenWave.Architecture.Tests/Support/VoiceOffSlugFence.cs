using System.Text.Json;
using System.Text.RegularExpressions;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// Source scan behind law L12 (SPEC F214.16/F214.17, STORY-501, PLAN T619): <c>tools/VoiceOff</c>
/// names no engine slug outside <c>engines/*.json</c> (a new engine is one profile and one shim, no
/// C# change), and no image build or compose file mentions the tool. Plain text scans — comments
/// count, because a slug in a comment is still a slug the next engine must edit around.
/// </summary>
internal static class VoiceOffSlugFence
{
    private static readonly string[] ScannedPatterns = ["*.cs", "*.html", "*.csproj"];
    private static readonly string[] ExcludedSegments = ["engines", "shims", "gauntlet", "bin", "obj"];
    private static readonly string[] ImageScanExcludedSegments = [".claude", "node_modules", "bin", "obj", ".git"];

    /// <summary>Each <c>engines/*.json</c> profile's <c>"slug"</c>; a profile with no string slug throws.</summary>
    public static IReadOnlyList<string> Slugs(string engineDir) =>
        Directory.EnumerateFiles(engineDir, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(ReadSlug)
            .ToList();

    /// <summary>Every whole-word, case-sensitive slug hit in the tool's own code under
    /// <paramref name="voiceOffRoot"/>. <c>Member</c> is the repo-relative file (<c>tools/VoiceOff/…</c>), <c>Detail</c> names
    /// the slug and line.</summary>
    public static IReadOnlyList<LawViolation> Scan(string voiceOffRoot, IReadOnlyCollection<string> slugs)
    {
        var matchers = slugs.Select(slug => (Slug: slug, Pattern: new Regex(@"\b" + Regex.Escape(slug) + @"\b"))).ToList();
        var violations = new List<LawViolation>();

        foreach (var file in ScannedFiles(voiceOffRoot))
        {
            var relative = Path.GetRelativePath(voiceOffRoot, file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                foreach (var (slug, pattern) in matchers.Where(m => m.Pattern.IsMatch(lines[index])))
                {
                    violations.Add(new LawViolation(
                        LawId.L12, "tools/VoiceOff/" + relative, $"line {index + 1} names engine slug '{slug}' outside engines/*.json"));
                }
            }
        }

        return violations;
    }

    /// <summary>Every <c>Dockerfile*</c> in the repo and every root <c>compose*.yaml</c> that mentions
    /// <c>VoiceOff</c> (case-insensitive: <c>voiceoff</c> is the verb name).</summary>
    public static IReadOnlyList<LawViolation> ScanImages(string repoRoot)
    {
        var dockerfiles = Directory.EnumerateFiles(repoRoot, "Dockerfile*", SearchOption.AllDirectories)
            .Where(path => !HasSegment(Path.GetRelativePath(repoRoot, path), ImageScanExcludedSegments));
        var composes = Directory.EnumerateFiles(repoRoot, "compose*.yaml", SearchOption.TopDirectoryOnly);

        return dockerfiles.Concat(composes)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Where(path => File.ReadAllText(path).Contains("voiceoff", StringComparison.OrdinalIgnoreCase))
            .Select(path => new LawViolation(
                LawId.L12, Path.GetRelativePath(repoRoot, path).Replace('\\', '/'), "mentions VoiceOff; the tool never ships"))
            .ToList();
    }

    public static string Format(IEnumerable<LawViolation> violations) =>
        string.Join(Environment.NewLine, violations.Select(v => $"{v.LawId}: {v.Member} — {v.Detail}"));

    private static IEnumerable<string> ScannedFiles(string root) =>
        ScannedPatterns
            .SelectMany(pattern => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            .Where(path => Path.GetFileName(path) != "RESULTS.md")
            .Where(path => !HasSegment(Path.GetRelativePath(root, path), ExcludedSegments))
            .OrderBy(path => path, StringComparer.Ordinal);

    private static bool HasSegment(string relativePath, string[] excluded) =>
        relativePath.Split('/', '\\').SkipLast(1).Any(segment => excluded.Contains(segment, StringComparer.Ordinal));

    private static string ReadSlug(string profilePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(profilePath));
        if (document.RootElement.TryGetProperty("slug", out var slug) && slug.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(slug.GetString()))
        {
            return slug.GetString() ?? throw new InvalidOperationException(profilePath);
        }

        throw new InvalidOperationException($"{profilePath}: engine profile has no \"slug\" string.");
    }
}
