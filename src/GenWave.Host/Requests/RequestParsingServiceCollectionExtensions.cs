namespace GenWave.Host.Requests;

using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI wiring for the wish-parse pipeline (SPEC F87.4, STORY-225, PLAN T88): the bounded queue between
/// <see cref="Api.SpectatorRequestsController"/> (producer — a fresh row's id) and
/// <see cref="RequestParserService"/> (consumer), the two <see cref="IWishParser"/> implementations,
/// and the hosted service itself. MUST run after <c>AddGenWaveTts</c> (needs
/// <c>IDegradationModeReader</c>/<c>LlmOptions</c>) — mirrors <c>AddGenWaveMoodTaggingGate</c>'s own
/// ordering note.
///
/// <para>
/// The queue is registered as bare <see cref="ChannelReader{T}"/>/<see cref="ChannelWriter{T}"/>
/// singletons, deliberately NOT the enclosing <see cref="Channel{T}"/> itself — contrast
/// <c>BoothLogServiceCollectionExtensions</c>'s own <c>Channel&lt;BoothLogEntryRequest&gt;</c>
/// registration. <c>GenWave.MediaLibrary.MediaLibraryServiceCollectionExtensions</c> already owns the
/// ONE <see cref="Channel{T}"/> of <see langword="long"/> in this container (the enrich-delta queue
/// <c>EnrichmentService</c> drains) — a second bare <c>Channel&lt;long&gt;</c> registration here
/// would make <c>GetRequiredService&lt;Channel&lt;long&gt;&gt;()</c> ambiguous (the container resolves
/// the LAST registration for a single-instance request), silently handing <c>EnrichmentService</c>
/// THIS queue instead of its own. Registering only the reader/writer projections sidesteps the
/// collision entirely — nothing else in this container binds a bare <see cref="ChannelReader{T}"/>/
/// <see cref="ChannelWriter{T}"/> of <see langword="long"/>.
/// </para>
///
/// <para>
/// Bounded to 64 with <see cref="BoundedChannelFullMode.DropOldest"/> — not <c>Wait</c> (an anonymous
/// POST must never block on parsing capacity) and not the default drop-newest behavior: the intake
/// controller's write is a fire-and-forget prompt-parse nudge, not a durable work item —
/// <see cref="RequestParserService.RecoverPendingAsync"/> already re-derives any row a crash/restart
/// drops from the durable <c>station.request</c> table itself (the
/// <c>EnrichmentService.RequeuePendingAsync</c> precedent), so a dropped id only delays that one row
/// until the next restart's recovery sweep — and under a sustained backlog, a fresher request
/// mattering more than a stale one is exactly what dropping the OLDEST buys.
/// </para>
/// </summary>
static class RequestParsingServiceCollectionExtensions
{
    public static IServiceCollection AddGenWaveRequestParsing(this IServiceCollection services)
    {
        var channel = Channel.CreateBounded<long>(
            new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
        services.AddSingleton(channel.Reader);
        services.AddSingleton(channel.Writer);

        services.AddSingleton<DeterministicWishParser>();
        services.AddSingleton<LlmWishParser>();
        services.AddHostedService<RequestParserService>();

        return services;
    }
}
