// gh-#129 — public-surface limiters shared one partition: Caddy stripped XFF (no
// trusted_proxies) and ForwardLimit=1 truncated the trusted-hop walk on the inner proxy.
//
// BDD specification — xUnit. Config-regression pins: the ForwardedHeadersOptions posture per
// Proxy:TrustedNetworks presence, and the reference Caddyfile's trusted_proxies directive
// (Story221's file-content-guard idiom). The live two-hop resolution is verified at deploy
// (gh-#74's login line shows the resolved caller IP).

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GenWave.Host.Tests.Specs;

public static class FeatureForwardedHeadersTrustedHopWalk
{
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return dir.FullName;
    }

    sealed class ProxyConfiguredFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
            builder.UseSetting("Proxy:TrustedNetworks:0", "172.28.20.0/24");
        }
    }

    sealed class ProxyUnconfiguredFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        }
    }

    public sealed class ScenarioTrustedNetworksConfigured
    {
        [Fact]
        public async Task TheForwardWalkIsUnlimitedSoTrustDecidesWhereItStops()
        {
            await using var factory = new ProxyConfiguredFactory();
            _ = factory.CreateClient();

            var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

            Assert.Null(options.ForwardLimit);
        }

        [Fact]
        public async Task TheConfiguredNetworkIsAKnownNetwork()
        {
            await using var factory = new ProxyConfiguredFactory();
            _ = factory.CreateClient();

            var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

            Assert.Contains(options.KnownIPNetworks, n => n.ToString() == "172.28.20.0/24");
        }
    }

    public sealed class ScenarioNoTrustedNetworks
    {
        [Fact]
        public async Task TheDefaultOneHopLimitStays()
        {
            // Direct-exposure deployments keep the middleware's own conservative default —
            // an unlimited walk with only loopback trusted would change nothing, but pinning
            // the default makes the gh-#129 posture explicit in both directions.
            await using var factory = new ProxyUnconfiguredFactory();
            _ = factory.CreateClient();

            var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

            Assert.Equal(1, options.ForwardLimit);
        }
    }

    public sealed class ScenarioCaddyfileTrustsItsHop
    {
        [Fact]
        public void TheReferenceCaddyfileDeclaresTrustedProxies()
        {
            // Without this directive Caddy v2.5+ strips the inbound X-Forwarded-For and the
            // real client IP never reaches the api at all (gh-#129's other half).
            var caddyfile = File.ReadAllText(Path.Combine(RepoRoot(), "Caddyfile"));

            Assert.Contains("trusted_proxies", caddyfile);
        }
    }
}
