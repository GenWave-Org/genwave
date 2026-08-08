using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// T216 (SEAMS.md generator, STORY-294, SPEC F105.6): builds the REAL <see cref="IServiceCollection"/>
/// Program.cs's composition root produces — every <c>Add*</c>/<c>AddGenWave*</c> extension call, run
/// for real, in Program.cs's own order — and hands back only the GenWave-owned ports a caller asks
/// for, each resolved to its actual concrete adapter type.
///
/// <b>Why this lives here, not in <c>tools/SeamIndexGenerator</c> directly.</b> The REAL blocker is
/// <c>Program</c> itself: top-level statements compile to an <c>internal partial class Program</c> by
/// default, and <c>GenWave.Host.csproj</c>'s one <c>InternalsVisibleTo</c> names exactly one assembly
/// — <c>GenWave.Host.Tests</c>. <c>WebApplicationFactory&lt;Program&gt;</c> is therefore uninstantiable
/// from any other assembly, full stop — it would stay that way even if every one of Program.cs's own
/// internal wiring extensions (<c>StationOptionsServiceCollectionExtensions</c>,
/// <c>AdminApiServiceCollectionExtensions</c>, <c>PlayoutServiceCollectionExtensions</c>,
/// <c>StationSettingsHostingExtensions</c>, ...) were made public tomorrow. T216 must not touch
/// <c>src/</c> (a new <c>InternalsVisibleTo</c> entry would count), so the only honest way to see the
/// REAL, complete registration graph — rather than a generator-side re-typing of Program.cs's call
/// list that can silently drift — is to build it from inside the one assembly that already has
/// legitimate access, and hand back nothing but public BCL types (<see cref="Type"/>,
/// <see cref="ServiceLifetime"/>). <c>tools/SeamIndexGenerator</c> and <c>GenWave.Architecture.Tests</c>
/// both reach this through a plain <c>ProjectReference</c> — see that project's own csproj comment
/// for a process-wide side effect this project's <c>DisableConfigFileWatchingModuleInitializer</c>
/// carries along for free.
///
/// <b>Mechanism.</b> Mirrors this project's own proven "minimal config, no DB" recipe
/// (<c>StatusApiWebFactory</c>, <c>SafeTrackWebFactory</c>): <c>Development</c> environment
/// (<c>appsettings.Development.json</c> supplies Station/Tts's required fields), a placeholder
/// <c>ConnectionStrings:Library</c> (<c>AddMediaLibrary</c>'s own <c>NpgsqlDataSourceBuilder</c>
/// never opens a socket merely by being constructed — same lazy contract every station-scoped store
/// in this codebase documents), and an <c>Admin:Password</c> (options validation runs inside
/// <c>Host.StartAsync</c> regardless of hosted services). Every <see cref="IHostedService"/> is
/// removed before the host starts — no scan, no TTS render, no Liquidsoap/Icecast reach, no
/// persona/theme/font boot load. Registered ports are then resolved to their concrete adapter type
/// by invoking each <see cref="ServiceDescriptor"/> directly (not through
/// <c>IServiceProvider.GetService</c>, which only ever returns the LAST registration) — this WILL run
/// a real <c>new SomeAdapter(...)</c> for every registration a port carries, not just a trivial
/// pass-through: several ports (<c>ILiquidsoapControl</c>, <c>IStationEventSink</c>,
/// <c>IStationSettingsStore</c>, <c>ITtsVoiceLister</c>, <c>IPersonaMemory</c>,
/// <c>IPatterDurationEstimator</c> among them) register a genuine <c>sp =&gt; new X(...)</c> factory,
/// not <c>sp =&gt; sp.GetRequiredService&lt;Concrete&gt;()</c>. Generation is inert today because
/// every one of those constructors happens to do no I/O (strace-verified: zero sockets opened) — an
/// INVARIANT this repo has to keep, not a shape guaranteed by the registration style. (Worth a future
/// architecture-fitness law over constructor bodies; not built here — T216 is a generator, not a new
/// law.) This deliberately does NOT assume the last registration always "wins" in the consuming sense
/// — some multiply-registered ports are a `TryAdd`-default later overridden (single-resolve
/// semantics), others are one leg of a fan-out consumed via `IEnumerable&lt;T&gt;` (every registration
/// stays active) — <see cref="SeamAdapterEntry.IsEffective"/> only ever claims "what a plain
/// <c>GetService&lt;T&gt;()</c> call returns," never "what every consumer resolves."
/// </summary>
public static class SeamCompositionSnapshot
{
    /// <summary>Every registered port matching <paramref name="isPort"/>, ordinal-sorted by the
    /// port type's full name, each carrying every registration ever made against it in the order
    /// Program.cs made them.</summary>
    public static IReadOnlyList<SeamPort> Capture(Func<Type, bool> isPort)
    {
        List<ServiceDescriptor>? registered = null;
        using var factory = new SnapshotWebFactory(services => registered = services.ToList());

        // The narrowest trigger that forces WebApplicationFactory<Program> to actually build the
        // host (where ConfigureTestServices — and therefore the capture above — runs): no HTTP
        // client, no listening socket.
        var provider = factory.Services;

        if (registered is null)
        {
            throw new InvalidOperationException(
                "ConfigureTestServices never ran — WebApplicationFactory<Program> did not build a host.");
        }

        return registered
            .Where(d => isPort(d.ServiceType))
            .GroupBy(d => d.ServiceType)
            .OrderBy(g => g.Key.FullName, StringComparer.Ordinal)
            .Select(g => new SeamPort(g.Key, ResolveAdapters(g.ToList(), provider)))
            .ToList();
    }

    static IReadOnlyList<SeamAdapterEntry> ResolveAdapters(IReadOnlyList<ServiceDescriptor> descriptors, IServiceProvider provider)
    {
        var entries = new List<SeamAdapterEntry>(descriptors.Count);
        for (var i = 0; i < descriptors.Count; i++)
        {
            entries.Add(new SeamAdapterEntry(
                ResolveAdapterType(descriptors[i], provider),
                descriptors[i].Lifetime,
                IsEffective: i == descriptors.Count - 1));
        }

        return entries;
    }

    /// <summary>The concrete type a single descriptor produces, however it was registered.</summary>
    static Type ResolveAdapterType(ServiceDescriptor descriptor, IServiceProvider provider) =>
        descriptor.ImplementationType
        ?? descriptor.ImplementationInstance?.GetType()
        ?? descriptor.ImplementationFactory?.Invoke(provider).GetType()
        ?? throw new InvalidOperationException($"No adapter type resolvable for '{descriptor.ServiceType}'.");

    sealed class SnapshotWebFactory(Action<IServiceCollection> onConfigured) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
            builder.UseSetting("Admin:Password", "seam-index-snapshot");

            builder.ConfigureTestServices(services =>
            {
                onConfigured(services);
                services.RemoveAll<IHostedService>();
            });
        }
    }
}
