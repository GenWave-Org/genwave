using System.Net;
using System.Net.Sockets;
using GenWave.VoiceOff.Serving;
using GenWave.VoiceOff.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace GenWave.VoiceOff.Tests.Serving;

/// <summary>The host must not take its environment from the machine it runs on: a LAN client never sees a developer error page.</summary>
public static class ServeHardeningTests
{
    [Collection("ProcessEnvironment")]
    public sealed class ScenarioTheMachineSaysDevelopment : IDisposable
    {
        private readonly ScopedEnvironmentVariable environment = new("ASPNETCORE_ENVIRONMENT", "Development");
        private readonly ServeScenario serve = new();
        private readonly HttpResponseMessage answer;
        private readonly string body;

        public ScenarioTheMachineSaysDevelopment()
        {
            // A directory where votes.jsonl belongs makes the append throw: a real 500 from a real route.
            Directory.CreateDirectory(serve.VotesPath);
            answer = serve.Vote(serve.PairIds()[0], "Dean", "a");
            body = answer.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        [Fact]
        public void TheEnvironmentIsProduction() => Assert.Equal("Production", serve.EnvironmentName);

        [Fact]
        public void TheFailureAnswers500() => Assert.Equal(HttpStatusCode.InternalServerError, answer.StatusCode);

        [Fact]
        public void TheAnswerShowsNoStackTrace() => Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);

        [Fact]
        public void TheAnswerNamesNoSourceFile() => Assert.DoesNotContain("GenWave", body, StringComparison.Ordinal);

        public void Dispose()
        {
            serve.Dispose();
            environment.Dispose();
        }
    }

    /// <summary>Starts the real host on loopback port 0 and says where it ended up listening.</summary>
    private static IReadOnlyList<Uri> BoundUrls(PairScenario run)
    {
        ServeOptions options = new(run.RunId, run.Root, 0, IPAddress.Loopback);
        using WebApplication app = ServeHost.Build(options, TimeProvider.System, new StringWriter());
        app.StartAsync().GetAwaiter().GetResult();
        try
        {
            IServerAddressesFeature? addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            return [.. (addresses?.Addresses ?? []).Select(a => new Uri(a))];
        }
        finally
        {
            app.StopAsync().GetAwaiter().GetResult();
        }
    }

    private static int FreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private static PairScenario PairedRun()
    {
        PairScenario run = new();
        if (run.Pair() != 0)
        {
            run.Dispose();
            throw new InvalidOperationException("The fixture run did not pair.");
        }

        return run;
    }

    [Collection("ProcessEnvironment")]
    public sealed class ScenarioAConfigFileNamesAnotherListener : IDisposable
    {
        private readonly PairScenario run = PairedRun();
        private readonly IReadOnlyList<Uri> bound;

        public ScenarioAConfigFileNamesAnotherListener()
        {
            File.WriteAllText(
                Path.Combine(run.SessionDirectory, "appsettings.json"),
                $$"""{"urls":"http://0.0.0.0:{{FreePort()}}","preferHostingUrls":"true"}""");
            bound = BoundUrls(run);
        }

        [Fact]
        public void OneEndpointIsBound() => Assert.Single(bound);

        [Fact]
        public void ItIsTheLoopbackOneThatWasAsked() => Assert.Equal("127.0.0.1", bound[0].Host);

        public void Dispose() => run.Dispose();
    }

    [Collection("ProcessEnvironment")]
    public sealed class ScenarioBEnvironmentNamesAnotherListener : IDisposable
    {
        private readonly PairScenario run = PairedRun();
        private readonly ScopedEnvironmentVariable urls = new("ASPNETCORE_URLS", $"http://0.0.0.0:{FreePort()}");
        private readonly ScopedEnvironmentVariable prefer = new("ASPNETCORE_PREFERHOSTINGURLS", "true");
        private readonly IReadOnlyList<Uri> bound;

        public ScenarioBEnvironmentNamesAnotherListener() => bound = BoundUrls(run);

        [Fact]
        public void OneEndpointIsBound() => Assert.Single(bound);

        [Fact]
        public void ItIsTheLoopbackOneThatWasAsked() => Assert.Equal("127.0.0.1", bound[0].Host);

        public void Dispose()
        {
            prefer.Dispose();
            urls.Dispose();
            run.Dispose();
        }
    }
}
