namespace GenWave.Host.Tests.Support;

/// <summary>
/// The ONE way a Host.Tests spec owns a scratch directory (gh-#710, STORY-441, PLAN T479/T480):
/// created under <see cref="System.IO.Path.GetTempPath"/> with the <c>gw-</c> prefix, deleted
/// recursively on <see cref="Dispose"/>, tolerant of a directory that already vanished.
/// </summary>
internal sealed class TempDir : IDisposable
{
    public const string Prefix = "gw-";

    bool disposed;

    public TempDir() => Path = Directory.CreateTempSubdirectory(Prefix).FullName;

    public string Path { get; }

    /// <summary>
    /// A scratch directory whose lifetime is the whole test process rather than one <c>using</c>
    /// block — the third lifetime shape after <c>using var</c> and fixture-owned — for a static helper that hands a fresh path to many call sites
    /// across a spec file — or across several — threading a per-call disposer through every one
    /// of them would balloon the diff for no behavioural gain. <see cref="Dispose"/> runs once,
    /// at <see cref="AppDomain.ProcessExit"/>, best-effort; a killed process leaves the directory
    /// for the startup <c>TempSweep</c> to reclaim, exactly like every other <c>gw-*</c> scratch
    /// dir.
    /// </summary>
    public static string CreateForProcessLifetime()
    {
        var dir = new TempDir();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => dir.Dispose();
        return dir.Path;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            // Already gone, or a transient handle elsewhere — nothing left for a test-scratch
            // directory's disposal to do.
        }
    }
}
