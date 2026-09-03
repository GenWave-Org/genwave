using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for the plugin-door scenarios — mirrors
/// <c>Story084_StatusEndpoint.StatusApiWebFactory</c>'s own shape (fakes for every Postgres-backed
/// <c>StatusController</c>/booth-log dependency, every hosted service removed), plus the two plugin-door
/// knobs (<c>Plugins:Enabled</c>/<c>Plugins:Root</c>) and any per-plugin setting a scenario needs
/// (<c>Plugins:{slug}:{key}</c>). <see langword="null"/> for either knob means "leave it unset" — the
/// closed-door scenarios' own distinction from "explicitly false".
///
/// <para>
/// Hoisted here from <c>Story386_PluginDoorVisibleAndAdditive.cs</c> (PLAN T397 review fold): that
/// file's own <c>file</c>-scoped copy could not cross files, so
/// <c>Story388_AdSpotSourceRegistrationOrder.cs</c> — the ads seam's own F7 registration-order proof,
/// which needs the IDENTICAL "real plugin door, real WebApplicationFactory&lt;Program&gt;" composition
/// — had grown a second, near-duplicate copy of it. This is the one shared home both files now use;
/// neither carries its own copy any more.
/// </para>
/// </summary>
internal sealed class PluginDoorWebFactory(
    string? pluginsRoot, bool? enabled, IReadOnlyDictionary<string, string>? pluginSettings = null)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-plugin-door";

    internal FakeBoothLogAppender BoothLog { get; } = new();
    internal CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        if (pluginsRoot is not null)
            builder.UseSetting("Plugins:Root", pluginsRoot);
        if (enabled is not null)
            builder.UseSetting("Plugins:Enabled", enabled.Value ? "true" : "false");
        foreach (var (key, value) in pluginSettings ?? new Dictionary<string, string>())
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            // No Liquidsoap/DB connections during this test — mirrors StatusApiWebFactory exactly.
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));

            services.RemoveAll<IMediaRotationSink>();
            services.AddSingleton<IMediaRotationSink>(new FakeMediaRotationSink());

            services.RemoveAll<IRotFindingStore>();
            services.AddSingleton<IRotFindingStore>(new FakeRotFindingStore());

            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());

            // The booth-log narrative row this suite's own facts assert on (STORY-386 AC4) — a
            // Postgres-backed IBoothLogAppender would otherwise fail Program.cs's own post-Build
            // NarratePluginDoorAsync call in this DB-free test.
            services.RemoveAll<IBoothLogAppender>();
            services.AddSingleton<IBoothLogAppender>(BoothLog);

            services.AddSingleton<ILoggerProvider>(Logs);
        });
    }
}
