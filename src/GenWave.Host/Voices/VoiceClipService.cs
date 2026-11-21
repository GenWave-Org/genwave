using System.Text.RegularExpressions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;

namespace GenWave.Host.Voices;

/// <summary>
/// Upload and delete of voice clips (SPEC F219.12, F219.13, F219.15; PLAN T756), behind <c>VoiceClipController</c>. An upload is
/// staged and normalized away from the clips folder's public names, then published as <c>&lt;slug&gt;.wav</c> and recorded: a clip that
/// fails to convert leaves no file and no row, and a database failure after the move removes the file again. The slug is made here
/// from the label, never taken from the client.
/// </summary>
public sealed partial class VoiceClipService(
    IVoiceClipStore store,
    VoiceClipFolder folder,
    VoiceClipNormalizer normalizer,
    IVoiceClipUsageReader usageReader,
    ILogger<VoiceClipService> logger)
{
    /// <summary>How many slugs (<c>name</c>, <c>name-2</c>, ...) are tried before an upload gives up.</summary>
    const int MaxSlugAttempts = 50;

    public async Task<VoiceClipUploadResult> UploadAsync(string label, Stream content, CancellationToken ct)
    {
        var inputPath = Path.Combine(Path.GetTempPath(), $"genwave-clip-{Guid.NewGuid():N}.upload");
        var stagingPath = folder.NewStagingPath();
        try
        {
            await using (var input = File.Create(inputPath))
                await content.CopyToAsync(input, ct);

            if (VoiceClipContainer.Sniff(await ReadHeaderAsync(inputPath, ct)) is not { } demuxer)
                return new VoiceClipUploadResult.Rejected("The file must be a WAV, FLAC or MP3 recording.");

            return await normalizer.NormalizeAsync(inputPath, demuxer, stagingPath, ct) switch
            {
                VoiceClipNormalizeResult.Normalized done => await PublishAsync(label, stagingPath, done.DurationMs, ct),
                VoiceClipNormalizeResult.Rejected rejected => new VoiceClipUploadResult.Rejected(rejected.Reason),
                var other => throw new InvalidOperationException($"Unhandled {nameof(VoiceClipNormalizeResult)} case {other.GetType().Name}."),
            };
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(stagingPath);
        }
    }

    public async Task<IReadOnlyList<VoiceClip>> ListAsync(CancellationToken ct) => await store.ListAsync(ct);

    public async Task<VoiceClipDeleteResult> DeleteAsync(string slug, CancellationToken ct)
    {
        if (!SlugShape().IsMatch(slug))
            return new VoiceClipDeleteResult.NotFound();

        var users = await usageReader.FindUsersAsync(slug, ct);
        if (users.Count > 0)
            return new VoiceClipDeleteResult.InUse(users);

        if (!await store.DeleteAsync(slug, ct))
            return new VoiceClipDeleteResult.NotFound();

        folder.Remove(slug);
        logger.LogInformation("Voice clip deleted slug={Slug}", slug);
        return new VoiceClipDeleteResult.Deleted();
    }

    async Task<VoiceClipUploadResult> PublishAsync(string label, string stagingPath, int durationMs, CancellationToken ct)
    {
        var baseSlug = VoiceClipSlug.FromLabel(label);
        for (var attempt = 1; attempt <= MaxSlugAttempts; attempt++)
        {
            var slug = VoiceClipSlug.Candidate(baseSlug, attempt);
            if (!folder.Publish(stagingPath, slug))
                continue;

            VoiceClip? row;
            try
            {
                row = await store.InsertAsync(
                    new NewVoiceClip(slug, label, VoiceClipFolder.FileNameFor(slug), durationMs, VoiceClipSources.Upload, null), ct);
            }
            catch
            {
                folder.Remove(slug);
                throw;
            }

            if (row is not null)
            {
                logger.LogInformation(
                    "Voice clip uploaded slug={Slug} label={Label} durationMs={DurationMs}", slug, LogSanitize.Strip(label), durationMs);
                return new VoiceClipUploadResult.Created(row);
            }

            // The slug has a row but its file was free: drift (volume recreated, file deleted by hand, starter row before its
            // file). Take our file back to staging and try the next suffix.
            folder.Unpublish(slug, stagingPath);
        }

        return new VoiceClipUploadResult.Rejected("There are too many clips with that name. Please use a different label.");
    }

    static async Task<byte[]> ReadHeaderAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var header = new byte[VoiceClipContainer.HeaderBytes];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        return header[..read];
    }

    [GeneratedRegex(@"\A[a-z0-9]+(-[a-z0-9]+)*\z")]
    private static partial Regex SlugShape();
}
