namespace GenWave.Ads;

/// <summary>
/// The ONE construction site for the preview root path (SPEC F174.4; PLAN T442 ruling) —
/// <see cref="AdRenderService"/> (writer), <c>AdsController.PreviewWav</c> (reader, GenWave.Host), and
/// <see cref="AdSpotLifecycleGuardianService"/> (deleter) all resolve <see cref="Resolve"/> and re-assert
/// <see cref="IsUnder"/> against exactly this, rather than each hand-rolling its own
/// <c>Path.Combine</c>/<c>GetFullPath</c>/<c>TrimEndingDirectorySeparator</c> sequence — a bare
/// <c>Path.Combine(roots.AuthoredRoot, "preview")</c> with no normalization at one of the three call
/// sites was exactly the kind of drift a shared helper closes off for good.
/// </summary>
public static class AdPreviewRoot
{
    /// <summary>The canonical, separator-normalized preview directory — <c>{AuthoredRoot}/preview</c>
    /// (<c>AdsOptions.PreviewRetentionDays</c>'s own remarks: SPEC F174.4's <c>{Ads:LibraryRoot}</c> is
    /// <see cref="AdSpotLocatorRoots.AuthoredRoot"/> in code, per PLAN T442 ruling).</summary>
    public static string Resolve(AdSpotLocatorRoots roots) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(roots.AuthoredRoot, "preview")));

    /// <summary>
    /// Separator-aware canonical-root check (the CodeQL path-injection "strong guard" shape —
    /// <c>JinglePackController.IsUnderCanonicalRoot</c>'s own precedent, reimplemented here rather than
    /// referenced across projects, same as that controller's own remarks describe) — every caller that
    /// serves or deletes a stored path calls this in the SAME method as that I/O call, immediately
    /// before touching disk. <paramref name="fullPath"/> must equal <paramref name="canonicalRoot"/>
    /// itself or begin with it plus exactly one directory separator, so a sibling directory that merely
    /// shares the root as a STRING PREFIX (e.g. <c>/root/previewX/a.wav</c> against the canonical root
    /// <c>/root/preview</c>) is never mistaken for "under" it.
    /// </summary>
    public static bool IsUnder(string canonicalRoot, string fullPath) =>
        fullPath == canonicalRoot
            || fullPath.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}
