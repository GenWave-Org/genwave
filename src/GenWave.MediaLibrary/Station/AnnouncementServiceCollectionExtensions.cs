using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using GenWave.Core.Abstractions;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// DI wiring for <see cref="AnnouncementRepository"/> (SPEC F143, STORY-357, PLAN T337).
/// Deliberately separate from <see cref="MediaLibraryServiceCollectionExtensions.AddMediaLibrary"/>:
/// <c>station.announcement</c> lives in the <c>station</c> schema/role (<c>station_svc</c>), not
/// <c>library</c> — the same "own connection string, own <see cref="Lazy{T}"/> data source" shape
/// every other station-schema store in this directory uses (<see cref="RequestServiceCollectionExtensions"/>,
/// <see cref="ShowServiceCollectionExtensions"/>, ...).
///
/// <b>Registered under <see cref="IAnnouncementStore"/> (PLAN T339), <see cref="IAnnouncementSource"/>
/// (PLAN T338/T341), AND <see cref="IAnnouncementLifecycle"/> (PLAN T343) in the ONE call below.</b>
/// T337 shipped this call keyed on the bare concrete type (no seam existed yet); T339 gave it the
/// <see cref="ShowServiceCollectionExtensions.AddShowStore"/>-shaped "key on the
/// <c>GenWave.Core.Abstractions</c> seam" registration every sibling in this directory uses, then
/// briefly split <c>IAnnouncementSource</c>'s own registration into a SEPARATE
/// <c>AddAnnouncementSource</c> call the caller had to remember to invoke, in order, after this one
/// (T341 review finding F9 — a call-order hazard: nothing enforced that ordering, and a caller that
/// forgot the second call silently shipped with no <see cref="IAnnouncementSource"/> registered at
/// all). Folding both registrations into this ONE method removes that hazard outright: the
/// repository is resolved internally, never re-exposed as a call-order contract.
/// </summary>
public static class AnnouncementServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AnnouncementRepository"/> as a singleton — resolvable as itself, under
    /// <see cref="IAnnouncementStore"/>, and (decorated by <paramref name="decorate"/>) under
    /// <see cref="IAnnouncementSource"/> — over a dedicated <see cref="NpgsqlDataSource"/> built from
    /// <paramref name="connectionString"/>. The data source build is wrapped in a <see cref="Lazy{T}"/>
    /// — mirrors every other station-schema store's own remarks: merely resolving the store must
    /// never be enough to trigger a connection attempt against an empty/dev-mode connection string.
    ///
    /// Also registers <see cref="AnnouncementStateTypeHandler"/> — this store's only consumer, so
    /// unlike <see cref="DateOnlyTypeHandler"/>'s shared <c>AddMediaLibrary</c> home, the registration
    /// lives here instead (see that handler's own remarks). <see cref="SqlMapper.AddTypeHandler{T}"/>
    /// writes into a static, process-wide dictionary keyed by type, so re-registering the same
    /// <see cref="AnnouncementStateTypeHandler.Instance"/> on a second call is a harmless overwrite.
    ///
    /// <para>
    /// <b>Why <paramref name="decorate"/> is REQUIRED, not optional (T341 review finding F9 — the
    /// prior split-call shape defaulted it to <see langword="null"/>, registering the inner
    /// undecorated).</b> SPEC F145.2's defense-in-depth vend refusal (declining while
    /// <c>Station:SpectatorMode</c> is on) is Host privacy state — this project has no reference to,
    /// and must never read, that concept (the SAME "MediaLibrary layer never reads Host privacy
    /// state" ruling this feature's own binding carry-forwards restate). This project's own ONE
    /// caller (<c>GenWave.Host.Configuration.StationSettingsHostingExtensions</c>) always supplies a
    /// real decorator (<c>SpectatorModeAnnouncementVendGuard</c>); a null-default here bought nothing
    /// but a silent path to shipping the guard's own refusal unwired, which is exactly what the prior
    /// split-call shape risked. A caller with genuinely no privacy state to enforce (a future non-Host
    /// embedding of this library) still passes <c>(inner, _) =&gt; inner</c> explicitly — an honest,
    /// visible no-op, never an implicit default.
    /// </para>
    /// </summary>
    public static IServiceCollection AddAnnouncementStore(
        this IServiceCollection services,
        string connectionString,
        Func<IAnnouncementSource, IServiceProvider, IAnnouncementSource> decorate)
    {
        SqlMapper.AddTypeHandler(AnnouncementStateTypeHandler.Instance);
        services.AddSingleton(
            _ => new AnnouncementRepository(new Lazy<NpgsqlDataSource>(() => new NpgsqlDataSourceBuilder(connectionString).Build())));
        services.AddSingleton<IAnnouncementStore>(sp => sp.GetRequiredService<AnnouncementRepository>());
        services.AddSingleton<IAnnouncementSource>(
            sp => decorate(sp.GetRequiredService<AnnouncementRepository>(), sp));

        // The lifecycle guardians' own seam (SPEC F143.2/.3, F144.5/.6, F145.2; PLAN T343) — no
        // decoration: unlike IAnnouncementSource's SpectatorMode vend refusal, none of T343's three
        // guardians (aired confirmation, the re-arm/expiry sweep, the flip's decline sweep) is
        // itself privacy-conditional — each one already runs at exactly the moment SPEC F143.2/F145.2
        // name (a genuine TrackAired, a periodic sweep, the flip itself), so there is no Host-side
        // refusal to wrap this registration in.
        return services.AddSingleton<IAnnouncementLifecycle>(sp => sp.GetRequiredService<AnnouncementRepository>());
    }
}
