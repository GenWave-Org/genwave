namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Domain;
using GenWave.Tts;

/// <summary>
/// Controllable <see cref="IFallbackProfileRenderer"/> double (gh-#147) — wraps
/// <see cref="FakeTtsSynthesizer"/> so hop renders keep the exact on-disk WAV/cache-hash behavior
/// the fallback specs already rely on, and records the <see cref="TtsFallbackProfile"/> each
/// render was handed so chain-order and voice-semantics specs can assert on it.
/// </summary>
public sealed class FakeProfileRenderer(string engine) : IFallbackProfileRenderer
{
    public FakeTtsSynthesizer Inner { get; } = new();

    public string Engine { get; } = engine;

    /// <summary>Every profile handed to <see cref="RenderAsync"/>, in arrival order — including
    /// attempts that subsequently threw.</summary>
    public List<TtsFallbackProfile> Profiles { get; } = [];

    /// <summary>Optional shared attempt journal — hand the SAME list to several renderers to
    /// assert cross-hop execution order; entries are "engine@endpoint".</summary>
    public List<string>? CallJournal { get; set; }

    /// <summary>When set, the renderer awaits this long (honoring the token) before rendering —
    /// pass <see cref="Timeout.InfiniteTimeSpan"/> to simulate a hung engine for the per-hop
    /// render-budget specs.</summary>
    public TimeSpan? DelayBeforeRender { get; set; }

    /// <summary>Completed renders only — an attempt that threw does not count (mirrors
    /// <see cref="FakeTtsSynthesizer.CallCount"/>).</summary>
    public int CallCount => Inner.CallCount;

    public string? LastText => Inner.LastText;

    public string? LastVoice => Inner.LastVoice;

    public Exception? ThrowOnNextCall
    {
        get => Inner.ThrowOnNextCall;
        set => Inner.ThrowOnNextCall = value;
    }

    public async Task<string> RenderAsync(TtsFallbackProfile profile, TtsRenderContext context, CancellationToken ct)
    {
        Profiles.Add(profile);
        CallJournal?.Add($"{Engine}@{profile.Endpoint}");

        if (DelayBeforeRender is { } delay)
            await Task.Delay(delay, ct);

        return await Inner.SynthesizeAsync(context.Text, context.Voice, ct);
    }
}
