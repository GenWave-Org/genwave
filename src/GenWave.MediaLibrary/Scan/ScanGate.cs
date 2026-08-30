using System.Diagnostics.CodeAnalysis;
using GenWave.Core.Abstractions;

namespace GenWave.MediaLibrary.Scan;

/// <summary>
/// The one process-wide <see cref="IScanGate"/> (SPEC F154.6; STORY-379; PLAN T380, gh-#529) —
/// wraps exactly one <see cref="SemaphoreSlim"/>, registered as a singleton so <see cref="ScanService"/>'s
/// own tick and <c>Garden.FileActions.FileActionExecutor</c> share the SAME mutual-exclusion primitive
/// rather than each holding its own (which would let a scan and a file action run concurrently — the
/// exact overlap F154.6 exists to prevent). Both entry shapes are thin wrappers over the one
/// semaphore; see <see cref="IScanGate"/>'s own remarks for why there are two.
/// </summary>
sealed class ScanGate : IScanGate
{
    readonly SemaphoreSlim semaphore = new(1, 1);

    public bool TryEnter([NotNullWhen(true)] out IDisposable? lease)
    {
        if (semaphore.Wait(0))
        {
            lease = new ScanGateLease(semaphore);
            return true;
        }

        lease = null;
        return false;
    }

    public async Task<IDisposable?> EnterAsync(TimeSpan timeout, CancellationToken ct) =>
        await semaphore.WaitAsync(timeout, ct) ? new ScanGateLease(semaphore) : null;
}
