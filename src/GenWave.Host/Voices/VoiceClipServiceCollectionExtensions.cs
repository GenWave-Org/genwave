namespace GenWave.Host.Voices;

/// <summary>DI wiring for voice-clip upload, delete and the boot-time starter set (SPEC F219.12/F219.13/F219.14, PLAN T756/T757). The store itself is <c>AddVoiceClipStore</c>.</summary>
public static class VoiceClipServiceCollectionExtensions
{
    public static IServiceCollection AddVoiceClips(this IServiceCollection services) =>
        services
            .AddSingleton<VoiceClipFolder>()
            .AddSingleton<VoiceClipNormalizer>()
            .AddSingleton<IVoiceClipUsageReader, VoiceClipUsageReader>()
            .AddSingleton<VoiceClipService>()
            .AddSingleton<StarterClipSeeder>()
            .AddHostedService<StarterClipSeedHostedService>();
}
