using System.Reflection;

namespace GenWave.Ads;

/// <summary>
/// Reads one line-per-entry embedded text resource (SPEC F160.3: "the list ships embedded in
/// GenWave.Ads, is data not config, and grows by PR") — <c>#</c>-prefixed and blank lines are comments,
/// everything else is a raw (unfolded) phrase. Shared loader for both <see cref="AdBrandBlocklist"/>
/// and <see cref="AdProfanityList"/> — the two lists differ only in their resource name and what they
/// guard, never in how they are read or matched.
/// </summary>
internal static class EmbeddedWordList
{
    public static IReadOnlyList<string> Load(string resourceName)
    {
        var assembly = typeof(EmbeddedWordList).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource \"{resourceName}\" was not found in {assembly.FullName}.");
        using var reader = new StreamReader(stream);

        var entries = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            entries.Add(trimmed);
        }

        return entries;
    }
}
