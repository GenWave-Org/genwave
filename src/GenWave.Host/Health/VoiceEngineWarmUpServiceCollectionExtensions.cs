namespace GenWave.Host.Health;

/// <summary>
/// Wires the active voice engine's boot warm-up line and reachability checks (SPEC F219.25). Separate from
/// <see cref="DependencyHealthServiceCollectionExtensions"/> because it needs the whole Tts graph
/// (<c>AddGenWaveTts</c> registers <c>VoiceEngineWarmUp</c>), while that one needs only the health store; the cadence
/// knobs are the shared <c>DependencyHealth</c> options it registers.
/// </summary>
public static class VoiceEngineWarmUpServiceCollectionExtensions
{
    public static IServiceCollection AddGenWaveVoiceEngineWarmUp(this IServiceCollection services)
    {
        services.AddHostedService<VoiceEngineWarmUpService>();
        return services;
    }
}
