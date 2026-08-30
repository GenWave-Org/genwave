using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Enrich;

namespace GenWave.MediaLibrary.Garden.FileActions;

/// <summary>
/// The Library Gardener's read-only tag probe (SPEC F154.1; STORY-379; PLAN T381 review N4,
/// gh-#529) — the <see cref="IFileTagReader"/> <see cref="FileActionPlanner"/> itself opens a file
/// through when a retag needs the file's own CURRENT tags to diff against the catalog
/// (<c>TagDiffCalculator</c> reasons entirely off the snapshot this returns), ONLY AFTER the
/// subject has already passed the jail's own destination gate — see
/// <see cref="IFileActionPlanner"/>'s own remarks.
///
/// A read failure — file missing, permission denied, or a corrupt/unrecognised container — returns
/// <see langword="null"/> rather than throwing: the caller treats a null reading exactly like a
/// tagless file (<c>TagDiffCalculator</c> already treats a null <see cref="FileTags"/> that way),
/// never a 500.
///
/// <see cref="TagText.Normalize"/> is reused verbatim (gh-#257's entity-decode seam, and the same
/// call <see cref="Enricher"/>'s own first-pass tag read already makes) — a retag diff must never
/// manufacture a spurious "change" against an entity-encoded value already on disk.
/// </summary>
sealed class FileTagReader : IFileTagReader
{
    public FileTags? TryRead(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;
            return new FileTags(
                TagText.Normalize(tag.JoinedPerformers),
                TagText.Normalize(tag.Title),
                TagText.Normalize(tag.Album),
                tag.Year > 0 ? tag.Year : null,
                TagText.Normalize(tag.JoinedGenres));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or TagLib.CorruptFileException or TagLib.UnsupportedFormatException)
        {
            return null;
        }
    }
}
