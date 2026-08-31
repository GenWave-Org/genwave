using System.Globalization;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Garden.FileActions;

/// <summary>
/// Builds a retag's tag diff (SPEC F154.1, F154.5; STORY-379; PLAN T381 review N4, gh-#529) — split
/// out of <see cref="FileActionPlanner"/> for cohesion. The catalog value always wins; a
/// <see langword="null"/> OR EMPTY/WHITESPACE catalog value never produces a <see cref="TagChange"/>
/// (T379 review N8 — never blanks a tag the catalog has no real opinion on). String fields compare
/// ordinal; <see cref="FileActionSubject.Year"/>
/// (catalog, <see cref="int"/>) and <see cref="FileTags.Year"/> (file, <see cref="uint"/>) compare
/// numerically, never by their string forms.
///
/// <paramref name="fileTags"/> is the caller's own reading of the file's CURRENT tags (T381 review
/// N4 — <see cref="FileActionPlanner"/> reads it via <see cref="GenWave.Core.Abstractions.IFileTagReader"/>
/// AFTER the subject's own destination gate has already passed, never before), <see langword="null"/>
/// when the read failed or the file is tagless — treated identically to every field being absent.
/// </summary>
static class TagDiffCalculator
{
    public static IReadOnlyList<TagChange> Compute(FileActionSubject subject, FileTags? fileTags)
    {
        var changes = new List<TagChange>();

        AddIfChanged(changes, "artist", fileTags?.Artist, subject.Artist);
        AddIfChanged(changes, "title", fileTags?.Title, subject.Title);
        AddIfChanged(changes, "album", fileTags?.Album, subject.Album);
        AddYearIfChanged(changes, fileTags?.Year, subject.Year);
        AddIfChanged(changes, "genre", fileTags?.Genre, subject.Genre);

        return changes;
    }

    static void AddIfChanged(List<TagChange> changes, string field, string? fileValue, string? catalogValue)
    {
        // An empty/whitespace catalog value reads as "no opinion" too (T379 review N8) — the same
        // "never blank a tag from a null" rule this method's own class doc already states, just
        // widened to cover the sparse-write shape's other common no-value spelling.
        if (string.IsNullOrWhiteSpace(catalogValue)) return;
        if (string.Equals(fileValue, catalogValue, StringComparison.Ordinal)) return;
        changes.Add(new TagChange(field, fileValue, catalogValue));
    }

    static void AddYearIfChanged(List<TagChange> changes, uint? fileYear, int? catalogYear)
    {
        if (catalogYear is null) return;

        var fileYearAsInt = fileYear.HasValue ? (int)fileYear.Value : (int?)null;
        if (fileYearAsInt == catalogYear) return;

        changes.Add(new TagChange(
            "year",
            fileYear?.ToString(CultureInfo.InvariantCulture),
            catalogYear.Value.ToString(CultureInfo.InvariantCulture)));
    }
}
