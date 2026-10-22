using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Render;

/// <summary>Finds the newest earlier run of the same tier under the root that kept a <c>timing.jsonl</c>, and reads its median clip time. Read-only.</summary>
internal static class PreviousRunFinder
{
    internal static async Task<RunSpeed> FindAsync(string root, EngineTier tier, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(root);
        string? newest = NewestWithTiming(root, tier);
        if (newest is null)
        {
            return RunSpeed.Unknown("no previous run");
        }

        try
        {
            IReadOnlyList<TimingRecord> records = await new TimingLog(new RunLayout(root, newest).TimingPath).ReadAsync(ct);
            double[] walls = records.Where(r => r.Ok).Select(r => r.WallMs).Order().ToArray();
            return walls.Length == 0
                ? RunSpeed.Unknown($"run {newest} has no finished clips")
                : RunSpeed.Known(newest, RtfSummary.Percentile(walls, 0.5));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return RunSpeed.Unknown($"timing.jsonl of run {newest} is unreadable");
        }
    }

    private static string? NewestWithTiming(string root, EngineTier tier)
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
                .Where(name => RunId.TryParseTier(name, out EngineTier found) && found == tier)
                .Where(name => File.Exists(new RunLayout(root, name).TimingPath))
                .OrderByDescending(name => name, StringComparer.Ordinal)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
