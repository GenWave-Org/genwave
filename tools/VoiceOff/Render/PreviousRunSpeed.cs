using System.Globalization;

namespace GenWave.VoiceOff.Render;

/// <summary>How fast the newest earlier run rendered a paragraph, or why that is not known: the basis of a dry run's ETA. Read-only.</summary>
internal sealed record PreviousRunSpeed(string? RunId, double MedianWallMs, string? UnknownReason)
{
    /// <summary>The newest run under <paramref name="root"/> (by when its <c>renders.jsonl</c> last changed) and the median wall time of its good paragraphs.</summary>
    internal static async Task<PreviousRunSpeed> FindAsync(string root, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(root);
        string? newest = Newest(root);
        if (newest is null)
        {
            return Unknown("no previous run");
        }

        try
        {
            IReadOnlyList<RenderRecord> records = await new RenderLog(new RunLayout(root, newest).RendersPath).ReadAsync(ct);
            double[] walls = records.Where(r => r.Ok).Select(r => r.WallMs).ToArray();
            return walls.Length == 0
                ? Unknown($"run {newest} has no finished paragraphs")
                : new PreviousRunSpeed(newest, RenderProgress.Median(walls), null);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return Unknown($"renders.jsonl of run {newest} is unreadable");
        }
    }

    /// <summary>The ETA line for <paramref name="paragraphs"/> paragraphs.</summary>
    internal string EtaLine(int paragraphs) => RunId is null
        ? $"ETA unknown ({UnknownReason})"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"ETA ~{RenderProgress.EtaMinutes(MedianWallMs, paragraphs):0} min (median {MedianWallMs / 1000:0.0} s per paragraph from run {RunId})");

    private static PreviousRunSpeed Unknown(string reason) => new(null, 0, reason);

    private static string? Newest(string root)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        try
        {
            return Directory.EnumerateDirectories(root)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Select(name => (Name: name, Path: new RunLayout(root, name).RendersPath))
                .Where(run => File.Exists(run.Path))
                .OrderByDescending(run => File.GetLastWriteTimeUtc(run.Path))
                .ThenByDescending(run => run.Name, StringComparer.Ordinal)
                .Select(run => run.Name)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
