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
///
/// <para>
/// <see cref="TryGetDeviceId"/> can also LIE (T380 review B4): a real cross-device filesystem
/// boundary is not arrangeable in a test (no bind mount), so <see cref="DeviceIdOverrides"/> lets a
/// fact assign whatever device id it wants to a given path; a path with no override falls through to
/// the real probe (same device for every path under one real temp root, in practice).
/// <see cref="DeviceIdUnknownPaths"/> (T380 review R2-3) forces an INCONCLUSIVE lookup — simulating a
/// failed <c>statx</c> — for a given path, WHILE the test still genuinely runs on Linux, distinct
/// from lying about the value.
/// </para>
/// </summary>
public sealed class RecordingFileSystemProbe(IFileSystemProbe inner) : IFileSystemProbe
{
    public bool KindWasCalled { get; private set; }

    public bool ResolveLinksWasCalled { get; private set; }

    public Dictionary<string, ulong> DeviceIdOverrides { get; } = [];

    public HashSet<string> DeviceIdUnknownPaths { get; } = [];

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

    public bool TryGetDeviceId(string path, out ulong deviceId)
    {
        if (DeviceIdUnknownPaths.Contains(path))
        {
            deviceId = 0;
            return false;
        }

        if (DeviceIdOverrides.TryGetValue(path, out deviceId))
            return true;
        return inner.TryGetDeviceId(path, out deviceId);
    }
}
