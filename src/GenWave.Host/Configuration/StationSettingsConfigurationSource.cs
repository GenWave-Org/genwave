using Microsoft.Extensions.Configuration;

namespace GenWave.Host.Configuration;

/// <summary>
/// <see cref="IConfigurationSource"/> that produces a <see cref="StationSettingsConfigurationProvider"/>
/// and exposes it via <see cref="BuiltProvider"/> so the DI-registered
/// <see cref="IStationSettingsStore"/> can call <see cref="StationSettingsConfigurationProvider.Reload"/>
/// after a write.
/// </summary>
public sealed class StationSettingsConfigurationSource : IConfigurationSource
{
    readonly string connectionString;

    // The single provider instance, accessible after Build() is called by the config system.
    StationSettingsConfigurationProvider? builtProvider;

    public StationSettingsConfigurationSource(string connectionString)
    {
        this.connectionString = connectionString;
    }

    /// <summary>
    /// The provider built by the last call to <see cref="Build"/>. Non-null after the configuration
    /// builder has called <see cref="Build"/> (which happens during <c>builder.Build()</c>).
    /// </summary>
    public StationSettingsConfigurationProvider? BuiltProvider => builtProvider;

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        builtProvider = new StationSettingsConfigurationProvider(connectionString);
        return builtProvider;
    }
}
