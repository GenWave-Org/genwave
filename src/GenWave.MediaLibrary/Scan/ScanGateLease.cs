namespace GenWave.MediaLibrary.Scan;

/// <summary>
/// The lease <see cref="ScanGate"/> hands back on entry — releasing the underlying
/// <see cref="SemaphoreSlim"/> exactly once, however many times <see cref="Dispose"/> is called
/// (a double-release would corrupt the semaphore's own count).
/// </summary>
sealed class ScanGateLease(SemaphoreSlim semaphore) : IDisposable
{
    int released;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref released, 1) == 0)
            semaphore.Release();
    }
}
