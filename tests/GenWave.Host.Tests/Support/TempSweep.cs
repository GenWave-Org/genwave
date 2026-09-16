namespace GenWave.Host.Tests.Support;

/// <summary>
/// Startup sweep for the stragglers (gh-#710, STORY-441, PLAN T479): removes every directory
/// directly under <c>root</c> whose name starts with one of <see cref="Prefixes"/> and whose last
/// write is older than <c>olderThan</c>. Run once per Host.Tests process from a module initializer.
/// </summary>
internal static class TempSweep
{
    public static readonly string[] Prefixes = ["gw-", "genwave-pawire-", "story343-env-", "gh332-"];

    /// <summary>Returns the number of directories removed.</summary>
    public static int Run(string root, TimeSpan olderThan)
    {
        if (!Directory.Exists(root))
            return 0;

        var cutoff = DateTime.UtcNow - olderThan;
        var removed = 0;

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(directory);
            if (!Prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
                continue;

            if (Directory.GetLastWriteTimeUtc(directory) >= cutoff)
                continue;

            try
            {
                Directory.Delete(directory, recursive: true);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Another process still holds this one open (or lost a race deleting it) —
                // leave it for the next sweep rather than failing the whole run over it.
            }
        }

        return removed;
    }
}
