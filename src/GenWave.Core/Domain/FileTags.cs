namespace GenWave.Core.Domain;

/// <summary>
/// A file's OWN tags, as read off disk (SPEC F154.1; STORY-379; PLAN T379/T381 review N4, gh-#529)
/// — <see cref="Abstractions.IFileActionPlanner"/> reads these itself for a retag, via
/// <see cref="Abstractions.IFileTagReader"/>, only AFTER the subject has already passed the jail's
/// own destination gate (T381 review N4: moved here from the caller, so a refused subject is never
/// opened at all). <see cref="Year"/> is <see cref="uint"/> to match TagLib's own <c>Tag.Year</c>
/// property exactly, unlike <see cref="FileActionSubject.Year"/>'s catalog-side <see cref="int"/>.
/// </summary>
/// <param name="Artist">The file's own artist tag, or <see langword="null"/> when absent.</param>
/// <param name="Title">The file's own title tag, or <see langword="null"/> when absent.</param>
/// <param name="Album">The file's own album tag, or <see langword="null"/> when absent.</param>
/// <param name="Year">The file's own year tag, or <see langword="null"/> when absent/zero.</param>
/// <param name="Genre">The file's own genre tag, or <see langword="null"/> when absent.</param>
public sealed record FileTags(string? Artist, string? Title, string? Album, uint? Year, string? Genre);
