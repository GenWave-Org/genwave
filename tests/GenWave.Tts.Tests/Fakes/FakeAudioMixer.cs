namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// Records the request it was called with and writes placeholder bytes to
/// <see cref="AudioMixRequest.OutputPath"/> so callers that stat the artifact (SizeBytes/Mtime) see a
/// real file, without needing a real ffmpeg invocation.
/// </summary>
public sealed class FakeAudioMixer : IAudioMixer
{
    public int Calls { get; private set; }
    public AudioMixRequest? LastRequest { get; private set; }

    /// <summary>When non-null, the next call to MixAsync will throw this exception and write nothing.</summary>
    public Exception? ThrowOnNextCall { get; set; }

    public Task MixAsync(AudioMixRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        LastRequest = request;

        if (ThrowOnNextCall is { } ex)
        {
            ThrowOnNextCall = null;
            throw ex;
        }

        Calls++;
        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath) ?? ".");
        File.WriteAllBytes(request.OutputPath, [1, 2, 3, 4]);
        return Task.CompletedTask;
    }
}
