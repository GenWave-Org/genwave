using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Tests.Fakes;

/// <summary>
/// Wraps a REAL <see cref="IFileSystemProbe"/> and records whether <see cref="Kind"/> and
/// <see cref="ResolveLinks"/> were ever called — a spy, not a mock: every answer still comes from the
/// real filesystem (<c>GenWave.MediaLibrary.Garden.FileActions.FileSystemProbe</c>), only the CALL
/// itself is observed. Backs STORY-379 AC9/AC10's own "before any I/O" pin (T379 review N7): a
/// Traversal refusal must never reach EITHER probe method; a symlink-escape refusal must never reach
/// <see cref="Kind"/> (its own <see cref="ResolveLinks"/> calls are expected — they are how the
/// escape is detected in the first place).
/// </summary>
public sealed class RecordingFileSystemProbe(IFileSystemProbe inner) : IFileSystemProbe
{
    public bool KindWasCalled { get; private set; }

    public bool ResolveLinksWasCalled { get; private set; }

    public FileSystemEntryKind Kind(string path)
    {
        KindWasCalled = true;
        return inner.Kind(path);
    }

    public string? ResolveLinks(string path)
    {
        ResolveLinksWasCalled = true;
        return inner.ResolveLinks(path);
    }
}
