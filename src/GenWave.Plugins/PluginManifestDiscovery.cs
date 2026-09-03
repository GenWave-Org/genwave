namespace GenWave.Plugins;

/// <summary>
/// Enumerates plugin manifest candidates directly beneath a plugins root (SPEC F156.2, STORY-385 AC2):
/// manifest-driven, never scan-driven. One directory level only — a candidate directory's own
/// subdirectories are never descended into, and any file sitting loose at the root (never inside a
/// <c>&lt;slug&gt;</c> directory) is never probed, whether or not it happens to be named
/// <see cref="ManifestFileName"/>. Filesystem read-only: this type never opens, parses, or validates a
/// manifest's contents — <see cref="PluginManifestParser"/> does that, once a candidate's file text has
/// been read.
///
/// <para>
/// <b>A symlinked child directory is never a candidate</b> — gh-#650's fail-closed posture, the same
/// one <c>GenWave.MediaLibrary.Garden.FileActions.FileActionPlanner</c>'s own jail takes for a
/// symlinked path segment (SPEC F154.3): an operator-mounted plugins root is exactly the kind of
/// directory a container image or a bind mount can smuggle a symlink into, whether aliasing a sibling
/// plugin directory (letting one manifest impersonate another slug) or escaping the root entirely.
/// Refusing it here, at discovery, means <see cref="PluginManifestParser"/> and the loader (PLAN T392)
/// never have to reason about it at all.
/// </para>
///
/// <para>
/// <b>Candidates yield in slug order</b>, <see cref="StringComparer.Ordinal"/> — not filesystem
/// enumeration order, which <see cref="Directory.EnumerateDirectories(string)"/> makes no guarantee
/// about. SPEC F156.6's "earlier plugin" tiebreak (e.g. two plugins racing to register the same
/// <c>IContextProvider</c> key) needs a stable, deterministic "earlier" — alphabetical by slug is that
/// order.
/// </para>
/// </summary>
public static class PluginManifestDiscovery
{
    /// <summary>The manifest file name a plugin directory must carry (SPEC F156.2).</summary>
    public const string ManifestFileName = "plugin.json";

    /// <summary>
    /// Yields one <see cref="PluginManifestCandidate"/> per immediate subdirectory of
    /// <paramref name="pluginsRoot"/> that itself contains a <see cref="ManifestFileName"/> file, in
    /// ascending slug order (this class's own remarks). A subdirectory without one is silently skipped
    /// here — it was never a candidate to begin with; the loader (PLAN T392) is what turns an unreadable
    /// or malformed MANIFEST into a WARN, once a candidate this method DID yield fails to parse. A
    /// symlinked subdirectory is skipped the same way, never even reaching the manifest-file check (this
    /// class's own remarks on gh-#650). A missing <paramref name="pluginsRoot"/> yields no candidates
    /// rather than throwing (SPEC F156.1: no mount at all is a normal, closed-door deployment shape, not
    /// an error).
    /// </summary>
    public static IEnumerable<PluginManifestCandidate> EnumerateCandidates(string pluginsRoot)
    {
        if (!Directory.Exists(pluginsRoot))
            yield break;

        // Directory.EnumerateDirectories defaults to SearchOption.TopDirectoryOnly — the "one
        // directory level, no recursion" rule holds without needing to state the option explicitly.
        // OrderBy imposes the slug order this class's own remarks require; enumeration order is
        // otherwise unspecified.
        var directories = Directory.EnumerateDirectories(pluginsRoot)
            .OrderBy(directory => Path.GetFileName(directory), StringComparer.Ordinal);

        foreach (var directory in directories)
        {
            // gh-#650: a symlinked child directory is never a candidate, fail-closed — see this
            // class's own remarks. LinkTarget is non-null exactly when the entry itself is a symbolic
            // link (FileSystemInfo's own contract), regardless of what it points at or whether that
            // target even exists.
            if (new DirectoryInfo(directory).LinkTarget is not null)
                continue;

            var manifestPath = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(manifestPath))
                continue;

            yield return new PluginManifestCandidate(Path.GetFileName(directory), manifestPath);
        }
    }
}
