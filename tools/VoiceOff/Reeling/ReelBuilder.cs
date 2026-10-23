using GenWave.Loudness;
using GenWave.VoiceOff.Audio;
using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Pairing;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Script;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Reeling;

/// <summary>
/// Builds <c>session/reels/</c>: one wav per engine, its plain-lane clips in gauntlet order with <see cref="GapSeconds"/> between them, loudness matched
/// as a whole and written in the canonical wav form. Built in <c>reels.partial</c> and renamed when whole, so a failure leaves no reels behind.
/// </summary>
internal sealed class ReelBuilder(RunLayout layout, UsableClips clips, LoudnessMatch match)
{
    internal const double GapSeconds = 0.6;
    internal const string FolderName = "reels";
    private const string PartialName = "reels.partial";

    internal static string PathIn(RunLayout layout) => Path.Combine(SessionFolder.PathIn(layout), FolderName);

    /// <returns>The reel of each engine under a fresh opaque id (in the order the engines were given), and how many gauntlet lines were left out for lack of a usable clip.</returns>
    /// <exception cref="InvalidDataException">An engine has no usable plain clip, or a reel could not be joined or matched.</exception>
    internal async Task<(IReadOnlyList<ManifestReel> Reels, int LeftOut)> BuildAsync(IReadOnlyList<string> engines, IReadOnlyList<GauntletLine> lines, CancellationToken ct)
    {
        string partial = Path.Combine(SessionFolder.PathIn(layout), PartialName);
        if (Directory.Exists(partial))
        {
            Directory.Delete(partial, recursive: true);
        }

        try
        {
            Directory.CreateDirectory(partial);
            List<ManifestReel> built = [];
            int leftOut = 0;
            foreach (string engine in engines)
            {
                string[] files = [.. lines.Where(l => clips.IsUsable(engine, RenderLane.Plain, l.Id)).Select(l => layout.WavPath(engine, RenderLane.Plain, l.Id))];
                leftOut += lines.Count - files.Length;
                if (files.Length == 0)
                {
                    throw new InvalidDataException("An engine in the wince set has no usable plain clips, so its reel cannot be built.");
                }

                string reelId = OpaqueId.New();
                await BuildOneAsync(files, Path.Combine(partial, reelId + ".wav"), ct);
                built.Add(new ManifestReel(reelId, engine));
            }

            Directory.Move(partial, PathIn(layout));
            return (built, leftOut);
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

    private async Task BuildOneAsync(string[] files, string final, CancellationToken ct)
    {
        string joined = final + ".joined";
        try
        {
            FfmpegRunResult join = await FfmpegProcess.RunFfmpegCapturingStderrAsync(ConcatArgs.Build(files, GapSeconds, joined), ct);
            if (!join.Succeeded)
            {
                throw new InvalidDataException($"ffmpeg could not join the clips into a reel (exit {join.ExitCode}).");
            }

            LoudnessMatchResult matched = await match.MatchAsync(joined, final, ct);
            if (!matched.Ok)
            {
                throw new InvalidDataException($"A reel could not be loudness matched: {matched.Error}.");
            }

            byte[] bytes = await File.ReadAllBytesAsync(final, ct);
            byte[] canonical = CanonicalWav.Rewrite(bytes, out string? problem)
                ?? throw new InvalidDataException($"A matched reel is not a usable wav: {problem}.");
            await File.WriteAllBytesAsync(final, canonical, ct);
        }
        finally
        {
            File.Delete(joined);
        }
    }
}
