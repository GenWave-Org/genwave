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
