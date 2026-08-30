using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Garden.FileActions;

/// <summary>
/// The real, read-only filesystem answer behind <see cref="IFileActionPlanner"/>'s jail (SPEC F154.3;
/// STORY-379; PLAN T379, gh-#529). <see cref="ResolveLinks"/> walks every ancestor segment of the
/// given absolute path from the root down — <see cref="FileSystemInfo.ResolveLinkTarget"/> alone only
/// resolves the LEAF entry, so a symlinked directory partway through the path (the exact escape
/// F154.3's mutation pin names) would otherwise go unnoticed. Neither <see cref="File"/> nor
/// <see cref="Directory"/> distinguishes file-vs-directory for the underlying <c>lstat</c>/
/// <c>readlink</c> syscalls <c>ResolveLinkTarget</c> issues, so a single <see cref="FileInfo"/> probe
/// per segment works for both.
/// </summary>
sealed class FileSystemProbe : IFileSystemProbe
{
    public FileSystemEntryKind Kind(string path)
    {
        if (Directory.Exists(path)) return FileSystemEntryKind.Directory;
        if (File.Exists(path)) return FileSystemEntryKind.File;
        return FileSystemEntryKind.Missing;
    }

    public string? ResolveLinks(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = Path.DirectorySeparatorChar.ToString();

        foreach (var segment in segments)
        {
            var candidate = Path.Combine(current, segment);
            FileSystemInfo? resolved;

            try
            {
                // ResolveLinkTarget(true) walks the whole chain to its final target in one call.
                resolved = new FileInfo(candidate).ResolveLinkTarget(returnFinalTarget: true);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // Nothing on disk at this segment yet — the common case for a rename/move TARGET,
                // which is by definition not there before the write. Nothing to resolve; carry the
                // candidate forward unresolved and keep walking the remaining segments.
                current = candidate;
                continue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A symlink cycle ("Too many levels of symbolic links", IOException) or a permission
                // failure partway through the chain (T379 review N2) — never trust this path. The
                // caller (FileActionPlanner) treats a null result as an automatic containment
                // failure, never a silent pass-through of the unresolved candidate.
                return null;
            }

            // Null here means the candidate exists but is not itself a symlink — the candidate as
            // built is already the right value to carry forward.
            current = resolved?.FullName ?? candidate;
        }

        return current;
    }
}
