using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Pairing;

/// <summary>
/// Writes <c>session/</c> (clips under random names, sessions.json, index.html) and gives back the truth table for <c>manifest.json</c>.
/// The folder is built as <c>session.partial</c> and renamed when whole, so a failed run leaves nothing that reads as paired.
/// </summary>
internal sealed class SessionFolder(RunLayout layout, string baseline, CanonicalClips clips)
{
    internal const string FolderName = "session";
    private const string PartialName = "session.partial";

    internal static string PathIn(RunLayout layout) => Path.Combine(layout.RunDirectory, FolderName);

    internal async Task<IReadOnlyList<ManifestPair>> WriteAsync(IReadOnlyList<IReadOnlyList<PlannedPair>> sessions, CancellationToken ct)
    {
        string partial = Path.Combine(layout.RunDirectory, PartialName);
        if (Directory.Exists(partial))
        {
            Directory.Delete(partial, recursive: true);
        }

        try
        {
            Directory.CreateDirectory(Path.Combine(partial, "clips"));
            List<ManifestPair> truth = [];
            List<SessionEntry> entries = [];
            for (int i = 0; i < sessions.Count; i++)
            {
                List<SessionPair> pairs = [];
                foreach (PlannedPair planned in sessions[i])
                {
                    ManifestPair pair = await WritePairAsync(partial, planned, ct);
                    truth.Add(pair);
                    pairs.Add(new SessionPair(pair.PairId, ClipPath(pair.A), ClipPath(pair.B)));
                }

                entries.Add(new SessionEntry((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), pairs));
            }

            await File.WriteAllTextAsync(Path.Combine(partial, "sessions.json"), StrictJson.Write(new SessionFile(SessionCutter.Cap, entries)), ct);
            await File.WriteAllTextAsync(Path.Combine(partial, "index.html"), ListeningPage.Html(), ct);
            Directory.Move(partial, PathIn(layout));
            return truth;
        }
        catch
        {
            if (Directory.Exists(partial))
            {
                Directory.Delete(partial, recursive: true);
            }

            throw;
        }
    }

    private static string ClipPath(string clipId) => "clips/" + clipId + ".wav";

    /// <summary>Two fresh clip copies per pair, even when the baseline clip is the same file: a reused id would give the baseline away.</summary>
    private async Task<ManifestPair> WritePairAsync(string partial, PlannedPair planned, CancellationToken ct)
    {
        string challengerWav = layout.WavPath(planned.Challenger, planned.Lane, planned.Line.Id);
        string baselineWav = layout.WavPath(baseline, RenderLane.Plain, planned.Line.Id);
        string a = await CopyClipAsync(partial, planned.BaselineIsA ? baselineWav : challengerWav, ct);
        string b = await CopyClipAsync(partial, planned.BaselineIsA ? challengerWav : baselineWav, ct);
        PairTruth truth = planned.BaselineIsA ? new PairTruth(baseline, planned.Challenger) : new PairTruth(planned.Challenger, baseline);
        return new ManifestPair(OpaqueId.New(), planned.Line.Id, planned.Line.Class.ToString(), planned.Lane, a, b, truth);
    }

    private async Task<string> CopyClipAsync(string partial, string source, CancellationToken ct)
    {
        byte[] canonical = clips.Get(source) ?? throw new InvalidDataException($"{source} is not a usable wav.");
        string clipId = OpaqueId.New();
        await File.WriteAllBytesAsync(Path.Combine(partial, "clips", clipId + ".wav"), canonical, ct);
        return clipId;
    }
}
