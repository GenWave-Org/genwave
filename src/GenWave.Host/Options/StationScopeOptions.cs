namespace GenWave.Host.Options;

/// <summary>
/// Library scope within the Station config section. Must contain at least one library id;
/// an empty scope produces a silent station (validated at startup).
/// </summary>
public sealed class StationScopeOptions
{
    public IList<long> LibraryIds { get; set; } = [];
}
