using GenWave.Tts;
using GenWave.VoiceOff.Audio;
using GenWave.Loudness;
using GenWave.VoiceOff.Script;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Render;

/// <summary>
/// Renders one gauntlet line for one engine and loudness-matches it into the run's <c>wav</c> folder
/// (SPEC F214.3, F214.4). A Crosstalk line is rendered turn by turn and joined with a pause. Never throws for an engine problem.
/// </summary>
internal sealed class ClipRenderer(SpeechClient client, EngineProfile profile, GauntletSet set, LoudnessMatch match, RunLayout layout)
{
    private static readonly double PauseSeconds = new TtsOptions().SentencePauseSeconds;

    internal async Task<ClipOutcome> RenderAsync(GauntletLine line, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(line);
        IReadOnlyList<(GauntletRegister Register, string Text)>? segments = Segments(line);
        if (segments is null)
        {
            return ClipOutcome.Failed("the gauntlet line has no text to speak", 0);
        }

        string raw = layout.RawPath(profile.Slug, line.Id);
        string final = layout.WavPath(profile.Slug, line.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(raw) ?? layout.RunDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(final) ?? layout.RunDirectory);

        (ClipOutcome? failure, double wallMs, WavInfo? info) = await RenderRawAsync(line, segments, raw, ct);
        if (failure is not null || info is null)
        {
            return failure ?? ClipOutcome.Failed("the audio could not be read", wallMs);
        }

        LoudnessMatchResult matched = await match.MatchAsync(raw, final, ct);
        return matched.Ok
            ? new ClipOutcome(true, null, null, wallMs, info.DurationMs, matched.Linear)
            : ClipOutcome.Failed(matched.Error ?? "loudness match failed", wallMs);
    }

    private static IReadOnlyList<(GauntletRegister, string)>? Segments(GauntletLine line)
    {
        if (line.Turns.Count > 0)
        {
            return line.Turns.Select(t => (t.Register, t.Text)).ToList();
        }

        return line.Register is GauntletRegister register && line.Text is string text ? [(register, text)] : null;
    }

    // Renders every segment, then writes the raw clip: the engine's bytes for one segment, the joined turns for several.
    // Info is the raw clip's header, read once: from the engine's bytes for one segment, from the joined file for several.
    private async Task<(ClipOutcome? Failure, double WallMs, WavInfo? Info)> RenderRawAsync(
        GauntletLine line, IReadOnlyList<(GauntletRegister Register, string Text)> segments, string raw, CancellationToken ct)
    {
        double wallMs = 0;
        List<string> turnFiles = [];
        WavInfo? lastTurnInfo = null;
        try
        {
            for (int i = 0; i < segments.Count; i++)
            {
                (GauntletRegister register, string text) = segments[i];
                SpeechResult result = await client.RenderAsync(
                    ClipTextBuilder.Build(text, profile, set), profile.VoiceFor(register), profile.Speed, ct);
                wallMs += result.WallMilliseconds;
                if (result.Error is { } error)
                {
                    return (ClipOutcome.Failed(error.Message, wallMs, error.Kind), wallMs, null);
                }

                string target = segments.Count == 1 ? raw : layout.RawPath(profile.Slug, line.Id, $".turn{i + 1}");
                if (segments.Count > 1)
                {
                    turnFiles.Add(target);
                }

                await File.WriteAllBytesAsync(target, result.Audio.ToArray(), ct);
                WavInfo? turnInfo = WavInfo.Read(result.Audio.Span, out string? problem);
                if (turnInfo is null)
                {
                    return (ClipOutcome.Failed(problem ?? "the audio could not be read", wallMs), wallMs, null);
                }

                lastTurnInfo = turnInfo;
            }

            if (segments.Count == 1)
            {
                return (null, wallMs, lastTurnInfo);
            }

            ClipOutcome? joinFailure = await JoinAsync(turnFiles, raw, wallMs, ct);
            return joinFailure is not null
                ? (joinFailure, wallMs, null)
                : (null, wallMs, WavInfo.Read(await File.ReadAllBytesAsync(raw, ct), out _));
        }
        finally
        {
            foreach (string turn in turnFiles)
            {
                File.Delete(turn);
            }
        }
    }

    private static async Task<ClipOutcome?> JoinAsync(List<string> turnFiles, string raw, double wallMs, CancellationToken ct)
    {
        FfmpegRunResult joined = await FfmpegProcess.RunFfmpegCapturingStderrAsync(ConcatArgs.Build(turnFiles, PauseSeconds, raw), ct);
        return joined.Succeeded ? null : ClipOutcome.Failed($"ffmpeg could not join the turns (exit {joined.ExitCode})", wallMs);
    }
}
