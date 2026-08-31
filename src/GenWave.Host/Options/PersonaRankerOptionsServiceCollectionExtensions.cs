namespace GenWave.Host.Options;

using Microsoft.Extensions.Options;
using GenWave.MediaLibrary.Options;
using GenWave.Orchestration;

/// <summary>
/// DI wiring for the SPEC F82 persona ranker (STORY-213, PLAN T64): binds <see cref="PersonaRankerOptions"/>
/// from the <c>PersonaRanker</c> appsettings section (PRD defaults — allowlist-visible settings are
/// NOT required by SPEC, so this task deliberately does not add any <c>PersonaRanker:*</c> key to
/// <see cref="GenWave.Host.Configuration.StationSettingsAllowlist"/>; no settings-API exposure) and
/// registers the real ranker chain — <see cref="SystemRandomSource"/>, <see cref="PersonaRanker"/>, and
/// <see cref="RankerPersonaPickProvider"/>.
///
/// <see cref="RankerPersonaPickProvider"/> is registered with a plain <c>AddSingleton</c> — never
/// <c>TryAdd</c> — deliberately AFTER <c>AddGenWaveOrchestration</c>'s own <c>TryAddSingleton&lt;IPersonaPickProvider,
/// NoOpPersonaPickProvider&gt;</c> has already run, so it wins the "last registration wins" resolution
/// .NET's container uses for a single (non-enumerable) dependency — <c>Orchestrator</c> takes exactly
/// one <c>IPersonaPickProvider?</c>, never an <c>IEnumerable&lt;IPersonaPickProvider&gt;</c>. Mirrors
/// the same override-after-the-default idiom <c>AddGenWaveOrchestration</c>'s own remarks document for
/// <c>INextItemProvider</c>: this call MUST run after <c>.AddGenWaveOrchestration()</c> in Program.cs.
///
/// <para>
/// <see cref="PersonaRankerOptions.NudgeGain"/> (SPEC F151.1/F155.1, STORY-371, PLAN T370) is the ONE
/// property on this options type NOT sourced from the <c>PersonaRanker</c> section above: architecture
/// law L1 keeps <c>GenWave.Orchestration</c> framework-free, so it cannot reference
/// <see cref="GardenerOptions"/> to read the gain directly. Instead, THIS composition root — which
/// already references both <c>GenWave.Orchestration</c> and <c>GenWave.MediaLibrary</c> — resolves the
/// ALREADY-bound, ALREADY-boot-validated <see cref="IOptions{TOptions}"/> of <see cref="GardenerOptions"/>
/// (MED-6, T370 review: <see cref="IOptions{TOptions}"/> resolution is lazy, so there is no
/// registration-order requirement to document or rely on — whichever of this call or
/// <c>.AddMediaLibrary</c> runs first in <c>Program.cs</c>, both singletons resolve correctly the
/// first time either is actually asked for) and copies its <see cref="GardenerOptions.NudgeGain"/>
/// straight onto the plain-value <see cref="PersonaRankerOptions"/> singleton below, via a
/// <c>with</c> expression (an init-only record property cannot be reassigned from a
/// <c>PostConfigure</c> callback, so the override happens here instead, at the ONE place this
/// singleton is built). One source of truth, unconditionally: no raw config key, no null branch, no
/// separate <c>PersonaRanker:NudgeGain</c> key is ever documented or read — the ranker's own
/// <see cref="PersonaRankerOptions.NudgeGain"/> IS <see cref="GardenerOptions.NudgeGain"/>, always.
/// </para>
/// </summary>
public static class PersonaRankerOptionsServiceCollectionExtensions
{
    public static IServiceCollection AddGenWavePersonaRanking(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<PersonaRankerOptions>, PersonaRankerOptionsValidator>();
        services
            .AddOptions<PersonaRankerOptions>()
            .Bind(configuration.GetSection("PersonaRanker"))
            .ValidateOnStart();
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PersonaRankerOptions>>().Value;
            var nudgeGain = sp.GetRequiredService<IOptions<GardenerOptions>>().Value.NudgeGain;
            return options with { NudgeGain = nudgeGain };
        });

        services.AddSingleton<IRandomSource, SystemRandomSource>();
        services.AddSingleton<PersonaRanker>();
        services.AddSingleton<IPersonaPickProvider, RankerPersonaPickProvider>();

        return services;
    }
}
