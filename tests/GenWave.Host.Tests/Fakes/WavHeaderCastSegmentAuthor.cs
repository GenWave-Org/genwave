using GenWave.Core.Domain;
using GenWave.Tts;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// <see cref="ICastSegmentAuthor"/> double for STORY-424's preview spec (PLAN T442). This Arc's own
/// WebFactory removes every hosted service but <c>AdSpotJobService</c>, so no background
/// <c>AdSpotWorker</c> render tick ever runs here — the only path this fixture drives is
/// <c>AdRenderService.RenderPreviewAsync</c>, which calls <see cref="AssembleOnlyAsync"/> alone;
/// <see cref="AuthorAsync"/> is unreached by design and throws rather than silently standing in for a
/// path this suite never exercises.
///
/// <para>
/// Named for what it actually returns, not merely "a fake" (PLAN T442 ruling) — unlike the
/// <c>GenWave.Ads.Tests</c> <c>FakeCastSegmentAuthor</c> precedent (a placeholder <c>[1, 2, 3, 4]</c>
/// byte array — sufficient there, since nothing in that project ever streams the bytes back over
/// HTTP), <see cref="AssembleOnlyAsync"/> here writes a REAL minimal RIFF/WAVE header so
/// <c>GET /api/ads/{id}/preview.wav</c>'s own facts can assert against genuine <c>audio/wav</c> bytes.
/// </para>
/// </summary>
sealed class WavHeaderCastSegmentAuthor : ICastSegmentAuthor
{
    public CastAssemblyRequest? LastAssembleOnlyRequest { get; private set; }

    public Task<CastSegmentAuthorResult> AuthorAsync(
        CastAssemblyRequest assemblyRequest,
        Func<CrosstalkAssemblyResult.Assembled, AuthoredMediaInsert> buildInsert,
        Func<long, CancellationToken, Task<bool>> confirmAsync,
        CancellationToken ct) =>
        throw new NotSupportedException(
            "this fixture's ICastSegmentAuthor fake exists only for preview mode (PLAN T442) — AuthorAsync is never reached");

    public Task<CrosstalkAssemblyResult> AssembleOnlyAsync(CastAssemblyRequest request, CancellationToken ct)
    {
        LastAssembleOnlyRequest = request;

        // AdRenderService.RenderPreviewCoreAsync creates the preview root itself before calling this
        // method (PLAN T442 ruling), but this fake also stands in for a caller that hasn't — writing
        // into a directory that already exists is a harmless no-op either way.
        Directory.CreateDirectory(request.OutputDirectory);
        var path = Path.Combine(request.OutputDirectory, $"{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(path, BuildMinimalWavBytes());

        CrosstalkAssemblyResult assembled = new CrosstalkAssemblyResult.Assembled(
            path, new GenWave.Core.Domain.Loudness(-16.0, -1.0, true), Cue: null, DurationMs: 1000);
        return Task.FromResult(assembled);
    }

    /// <summary>A minimal, valid 44-byte RIFF/WAVE header (PCM, mono, 8kHz, 16-bit) plus 100ms of
    /// silent payload — real enough for a caller reading the file back to see a genuine <c>RIFF</c>/
    /// <c>WAVE</c> structure, never an arbitrary placeholder.</summary>
    static byte[] BuildMinimalWavBytes()
    {
        const int sampleRate = 8000;
        const short bitsPerSample = 16;
        const short channels = 1;
        var payload = new byte[sampleRate / 10 * 2]; // 100ms of 16-bit mono silence

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + payload.Length);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * bitsPerSample / 8);
            writer.Write((short)(channels * bitsPerSample / 8));
            writer.Write(bitsPerSample);
            writer.Write("data"u8);
            writer.Write(payload.Length);
            writer.Write(payload);
        }

        return stream.ToArray();
    }
}
