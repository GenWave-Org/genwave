namespace GenWave.Host.Tests.Support;

/// <summary>
/// The ONE way a Host.Tests spec owns a scratch directory (gh-#710, STORY-441, PLAN T479/T480):
/// created under <see cref="System.IO.Path.GetTempPath"/> with the <c>gw-</c> prefix, deleted
/// recursively on <see cref="Dispose"/>, tolerant of a directory that already vanished.
/// </summary>
/// <remarks>Skeleton at plan time — throws until T479 lands.</remarks>
internal sealed class TempDir : IDisposable
{
    public const string Prefix = "gw-";

    public TempDir() =>
        throw new NotImplementedException("pending: T479 — TempDir (STORY-441)");

    public string Path => throw new NotImplementedException("pending: T479 — TempDir (STORY-441)");

    public void Dispose() =>
        throw new NotImplementedException("pending: T479 — TempDir (STORY-441)");
}
