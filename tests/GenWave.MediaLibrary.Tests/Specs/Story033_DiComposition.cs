// Verifies that AddMediaLibrary registers IEnergyAnalyzer so the DI container can construct
// Enricher without throwing — the regression that triggered Finding 1 of the E5 review.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GenWave.Core.Abstractions;
using GenWave.MediaLibrary.Enrich;

namespace GenWave.MediaLibrary.Tests.Specs;

public sealed class FeatureEnergyAnalyzerDiComposition
{
    // Minimal in-memory configuration: AddMediaLibrary needs a "Library" connection string
    // to avoid the startup guard. The actual connection is never opened in this test.
    static IServiceProvider BuildProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Library"] = "Host=localhost;Database=test;Username=u;Password=p"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediaLibrary(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void IEnergyAnalyzerIsResolvable()
    {
        var sp = BuildProvider();
        var analyzer = sp.GetRequiredService<IEnergyAnalyzer>();
        Assert.NotNull(analyzer);
    }

    [Fact]
    public void EnricherIsResolvable()
    {
        var sp = BuildProvider();
        var enricher = sp.GetRequiredService<Enricher>();
        Assert.NotNull(enricher);
    }
}
