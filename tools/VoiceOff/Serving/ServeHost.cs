using System.Globalization;
using GenWave.Core.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
    /// <param name="error">Where one line per failed request is written; the reply itself stays generic.</param>
    /// <param name="candidates">The network interfaces to choose the shown address from; null reads this machine's.</param>
    /// <exception cref="InvalidDataException">The run was not rendered, or a file of it is bad.</exception>
    internal static async Task<WebApplication> BuildAsync(
        ServeOptions options, TimeProvider time, TextWriter output, TextWriter error, CancellationToken ct, IReadOnlyList<NetworkCandidate>? candidates = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        ServeRun run = await ServeRun.OpenAsync(options, time, ct);

        // The host reads appsettings*.json from the working directory and ASPNETCORE_* variables by default. Neither may change what a LAN
        // sees (Development shows stack traces; a Kestrel:Endpoints section adds listeners), so the environment is fixed to Production,
        // every configuration source is dropped, and the only listener is the one --bind/--port name. The host read "urls" and "preferHostingUrls" from
        // those sources while it was being constructed, so the command-line pin below is what keeps a urls setting from replacing that listener
        // (the printed address is built from --bind and the bound port, which therefore cannot diverge). No watchers either: each costs an inotify instance.
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = ["--hostBuilder:reloadConfigOnChange=false", "--preferHostingUrls=false"],
            EnvironmentName = Environments.Production,
            ContentRootPath = run.RunDirectory,
        });
        builder.Configuration.Sources.Clear();

        // The console carries the one URL line and nothing else.
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = JsonBody.MaxBodyBytes;
            kestrel.Listen(options.Bind, options.Port);
        });

        TextWriter failures = TextWriter.Synchronized(error);
        WebApplication app = builder.Build();
        app.Use(async (http, next) =>
        {
            SecurityHeaders.Apply(http.Response);
            try
            {
                await next(http);
            }
            // A failure after the reply has started is not logged; that is accepted.
            catch (Exception ex) when (ex is not OperationCanceledException && !http.Response.HasStarted)
            {
                // Kestrel clears the headers of an unhandled failure, so the answer is rebuilt with them. The cause goes to the log line, not the body.
                failures.WriteLine(FailureLine(http.Request, ex));
                http.Response.Clear();
                SecurityHeaders.Apply(http.Response);
                http.Response.StatusCode = StatusCodes.Status500InternalServerError;
                http.Response.ContentType = "text/plain; charset=utf-8";
                await http.Response.WriteAsync("The server could not complete the request.", http.RequestAborted);
            }
        });
        ServeRoutes.Map(app, run);
        app.Lifetime.ApplicationStarted.Register(() => output.WriteLine(OpenLine(app, options, candidates ?? NetworkCandidates.Detect())));
        return app;
    }

    // One line: the message is free text, so CR and LF cannot start a second (forged) line.
    internal static string FailureLine(HttpRequest request, Exception ex) =>
        LogSanitize.Strip($"{request.Method} {request.Path} failed: {ex.GetType().Name}: {ex.Message}");

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
