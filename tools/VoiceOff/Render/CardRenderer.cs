using GenWave.Loudness;
using GenWave.VoiceOff.Audio;
using GenWave.VoiceOff.Deck;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Text;

namespace GenWave.VoiceOff.Render;

/// <summary>
/// One card, start to finish (SPEC F217.5): one request per paragraph carrying the card's voice, speed and settings,
/// each result kept as <c>raw/&lt;card&gt;/&lt;n&gt;.wav</c>, the eight joined with one fixed gap and loudness-matched into
/// <c>cards/&lt;card&gt;.wav</c>. A failure comes back as a <see cref="CardOutcome"/> naming the paragraph; nothing here throws for engine quality.
/// </summary>
internal sealed class CardRenderer(SpeechClient client, LoudnessMatch match, RunLayout layout, RenderLog log, RunProgress progress, TextWriter output)
{
    /// <summary>The silence between two paragraphs in the joined clip.</summary>
    internal const double GapSeconds = 0.5;

    internal async Task<CardOutcome> RenderAsync(CardPlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        List<string> rawPaths = [];
        foreach (ParagraphText paragraph in plan.Paragraphs)
        {
            ct.ThrowIfCancellationRequested();
            output.WriteLine("  " + progress.NextLine());
            (string path, CardOutcome? failure) = await RenderParagraphAsync(plan.Card, paragraph, ct);
            if (failure is not null)
            {
                progress.Drop(plan.Paragraphs.Count - rawPaths.Count - 1);
                return failure;
            }

            rawPaths.Add(path);
        }

        return await JoinAndMatchAsync(plan.Card.Id, rawPaths, ct);
    }

    private async Task<(string Path, CardOutcome? Failure)> RenderParagraphAsync(Card card, ParagraphText paragraph, CancellationToken ct)
    {
        SpeechResult result = await client.RenderAsync(paragraph.Text, card.Voice, card.Speed, card.Settings.Count == 0 ? null : card.Settings, ct);
        progress.Finished(result.WallMilliseconds);
        string path = layout.RawPath(card.Id, paragraph.Number);
        (double audioMs, string? problem) = result.Error is null ? await KeepAsync(path, result.Audio, ct) : (0, null);
        string? error = result.Error?.Message ?? problem;
        await log.AppendAsync(
            new RenderRecord(card.Id, paragraph.Number, paragraph.Text.Length, result.WallMilliseconds, audioMs, error is null, error),
            CancellationToken.None);
        if (result.Error is { } speechError)
        {
            return (path, CardOutcome.Failed($"paragraph {paragraph.Number} failed ({Label(speechError.Kind)}): {speechError.Message}", speechError.Kind));
        }

        return (path, problem is null ? null : CardOutcome.Failed($"paragraph {paragraph.Number} failed (unreadable audio): {problem}"));
    }

    // Writes the engine's bytes as returned when they are a usable WAV; otherwise says why not.
    private static async Task<(double AudioMs, string? Problem)> KeepAsync(string path, ReadOnlyMemory<byte> audio, CancellationToken ct)
    {
        if (WavInfo.Read(audio.Span, out string? problem) is not { } info)
        {
            return (0, problem);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        await File.WriteAllBytesAsync(path, audio.ToArray(), ct);
        return (info.DurationMs, null);
    }

    private async Task<CardOutcome> JoinAndMatchAsync(string cardId, List<string> rawPaths, CancellationToken ct)
    {
        string joined = layout.JoinedPath(cardId);
        string clip = layout.ClipPath(cardId);
        Directory.CreateDirectory(Path.GetDirectoryName(clip) ?? string.Empty);
        try
        {
            FfmpegRunResult join = await FfmpegProcess.RunFfmpegCapturingStderrAsync(ConcatArgs.Build(rawPaths, GapSeconds, joined), ct);
            if (!join.Succeeded)
            {
                return CardOutcome.Failed($"joining the paragraphs failed (ffmpeg exit {join.ExitCode})");
            }

            LoudnessMatchResult matched = await match.MatchAsync(joined, clip, ct);
            return matched.Ok ? CardOutcome.Success() : CardOutcome.Failed($"loudness match failed: {matched.Error}");
        }
        finally
        {
            File.Delete(joined);
        }
    }

    private static string Label(SpeechErrorKind kind) => kind switch
    {
        SpeechErrorKind.Unreachable => "unreachable",
        SpeechErrorKind.HttpStatus => "http status",
        SpeechErrorKind.Timeout => "timeout",
        SpeechErrorKind.EmptyBody => "empty body",
        SpeechErrorKind.TooLarge => "too large",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown speech error kind."),
    };
}
