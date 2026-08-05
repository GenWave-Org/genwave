namespace GenWave.Host.Tests.Fakes;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Stamps every request's <see cref="ConnectionInfo.LocalPort"/> so <c>SurfaceGateMiddleware</c> sees
/// the public listener — <c>TestServer</c> opens no real sockets, so this simulates
/// arrival-on-the-public-port by running before the production pipeline. The shared home for what had
/// drifted into three verbatim per-file copies (Story172_PublicListenerIsolation,
/// Story196_LlmCallInspector, Story278_ThemeCatalogIsolation; review finding, STORY-278/T190).
/// </summary>
sealed class SimulatedPortStartupFilter(int port) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.LocalPort = port;
            return nextMiddleware(context);
        });
        next(app);
    };
}
