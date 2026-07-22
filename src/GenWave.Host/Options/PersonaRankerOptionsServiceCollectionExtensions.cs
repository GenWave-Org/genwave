namespace GenWave.Host.Options;

using Microsoft.Extensions.Options;
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
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<PersonaRankerOptions>>().Value);

        services.AddSingleton<IRandomSource, SystemRandomSource>();
        services.AddSingleton<PersonaRanker>();
        services.AddSingleton<IPersonaPickProvider, RankerPersonaPickProvider>();

        return services;
    }
}
