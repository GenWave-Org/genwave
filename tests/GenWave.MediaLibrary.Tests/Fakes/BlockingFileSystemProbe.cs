using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Tests.Fakes;

/// <summary>
/// Wraps a REAL <see cref="IFileSystemProbe"/>; <see cref="Kind"/> blocks until <see cref="Release"/>
/// is called, signalling <see cref="WaitUntilEntered"/> the instant it is first called — lets a test
/// hold a <c>Garden.FileActions.FileActionExecutor</c> attempt inside its own gate-held window long
/// enough to observe a concurrent scan tick's own busy skip and a second executor's own bounded-wait
/// timeout (STORY-379's gate fact, PLAN T380). A spy on top of a block, not a mock — every answer
/// still comes from the real filesystem once released.
/// </summary>
public sealed class BlockingFileSystemProbe(IFileSystemProbe inner) : IFileSystemProbe
{
    readonly ManualResetEventSlim entered = new(initialState: false);
    readonly ManualResetEventSlim release = new(initialState: false);

    public FileSystemEntryKind Kind(string path)
    {
        entered.Set();
        release.Wait(TimeSpan.FromSeconds(10));
        return inner.Kind(path);
    }

    public string? ResolveLinks(string path) => inner.ResolveLinks(path);

    public bool TryGetDeviceId(string path, out ulong deviceId) => inner.TryGetDeviceId(path, out deviceId);

    /// <summary>Blocks until <see cref="Kind"/> has been entered at least once (or 5 seconds elapse
    /// — a safety bound, never expected to fire in a healthy run).</summary>
    public void WaitUntilEntered() => entered.Wait(TimeSpan.FromSeconds(5));

    public void Release() => release.Set();
}
