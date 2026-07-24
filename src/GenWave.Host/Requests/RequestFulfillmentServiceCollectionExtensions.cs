namespace GenWave.Host.Requests;

using GenWave.Orchestration;

/// <summary>
/// DI wiring for the real <see cref="RequestFulfillmentProvider"/> (SPEC F87.6, STORY-227, PLAN T90).
///
/// Registered with a plain <c>AddSingleton</c> — never <c>TryAdd</c> — deliberately AFTER
/// <c>AddGenWaveOrchestration</c>'s own <c>TryAddSingleton&lt;IRequestFulfillmentSource,
/// NoOpRequestFulfillmentSource&gt;</c> has already run, so it wins the "last registration wins"
/// resolution .NET's container uses for a single (non-enumerable) dependency —
/// <c>Orchestrator</c> takes exactly one <c>IRequestFulfillmentSource?</c>, never an
/// <c>IEnumerable&lt;IRequestFulfillmentSource&gt;</c>. Mirrors
/// <c>PersonaRankerOptionsServiceCollectionExtensions.AddGenWavePersonaRanking</c>'s own
/// override-after-the-default idiom exactly: this call MUST run after <c>.AddGenWaveOrchestration()</c>
/// in Program.cs. Every constructor dependency <see cref="RequestFulfillmentProvider"/> needs
/// (<c>IRequestStore</c>, <c>IRequestCatalogProbe</c>, <c>IRequestOverrideEnvelopeProvider</c>,
/// <c>IStationEventSink</c>, <c>TimeProvider</c>) is already registered by the time Program.cs reaches
/// this call.
/// </summary>
static class RequestFulfillmentServiceCollectionExtensions
{
    public static IServiceCollection AddGenWaveRequestFulfillment(this IServiceCollection services) =>
        services.AddSingleton<IRequestFulfillmentSource, RequestFulfillmentProvider>();
}
