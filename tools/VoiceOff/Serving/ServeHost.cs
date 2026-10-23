using System.Globalization;
using GenWave.VoiceOff.Render;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GenWave.VoiceOff.Serving;

/// <summary>The slim Kestrel host behind <c>serve</c>. Specs build it too, so they run the real routes in-process.</summary>
internal static class ServeHost
{
    /// <summary>Builds the host, not started. When it starts it prints where to open the page to <paramref name="output"/>.</summary>
    /// <param name="candidates">The network interfaces to choose the shown address from; null reads this machine's.</param>
    /// <exception cref="InvalidDataException">The run is missing, is not paired, or a file of it is bad.</exception>
    internal static WebApplication Build(ServeOptions options, TimeProvider time, TextWriter output, IReadOnlyList<NetworkCandidate>? candidates = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(output);

        ServeRun run = ServeRun.Open(options.Root, options.RunId);

        // The host reads appsettings*.json from the working directory and ASPNETCORE_* variables by default. Neither may change what a LAN
        // sees (Development shows stack traces; a Kestrel:Endpoints section adds listeners), so the environment is fixed to Production,
        // every configuration source is dropped, and the only listener is the one --bind/--port name. The host read "urls" and "preferHostingUrls" from
        // those sources while it was being constructed, so the command-line pin below is what keeps a urls setting from replacing that listener
        // (the printed address is built from --bind and the bound port, which therefore cannot diverge). No watchers either: each costs an inotify instance.
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = ["--hostBuilder:reloadConfigOnChange=false", "--preferHostingUrls=false"],
            EnvironmentName = Environments.Production,
            ContentRootPath = Path.GetDirectoryName(run.IndexPath),
        });
        builder.Configuration.Sources.Clear();

        // The console carries the one URL line and nothing else; a request line would carry a judge name.
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = ListeningRoutes.MaxBodyBytes;
            kestrel.Listen(options.Bind, options.Port);
        });

        WebApplication app = builder.Build();
        app.Use(async (http, next) =>
        {
            SecurityHeaders.Apply(http.Response);
            await next(http);
        });
        ListeningRoutes.Map(app, run, time);
        app.Lifetime.ApplicationStarted.Register(() => output.WriteLine(OpenLine(app, options, candidates ?? NetworkCandidates.Detect())));
        return app;
    }

    /// <summary>The port Kestrel actually bound; differs from the option when that was 0.</summary>
    internal static int BoundPort(WebApplication app)
    {
        IServerAddressesFeature? addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        string? first = addresses?.Addresses.FirstOrDefault();
        return first is not null && Uri.TryCreate(first, UriKind.Absolute, out Uri? uri)
            ? uri.Port
            : throw new InvalidOperationException("The server has not started.");
    }

    private static string OpenLine(WebApplication app, ServeOptions options, IReadOnlyList<NetworkCandidate> candidates)
    {
        // A specific --bind is the address to show; only "all interfaces" needs a LAN pick.
        LanChoice choice = options.Bind.Equals(System.Net.IPAddress.Any)
            ? LanAddress.Choose(candidates)
            : new LanChoice(options.Bind, false);
        string line = string.Create(CultureInfo.InvariantCulture, $"Open http://{choice.Address}:{BoundPort(app)}/ on a phone or laptop on this network.");
        return choice.IsFallback ? line + "\nNo network address was found, so this link only works on this computer." : line;
    }
}
