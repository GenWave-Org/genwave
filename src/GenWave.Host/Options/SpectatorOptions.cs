namespace GenWave.Host.Options;

/// <summary>
/// Configuration for the dedicated public listener (SPEC F64.1/F64.2, STORY-172). Bound from the
/// <c>Spectator</c> config section — env/compose-only, like <see cref="AdminOptions.Enabled"/> and
/// <see cref="ProxyOptions"/>: deliberately absent from
/// <see cref="GenWave.Host.Configuration.StationSettingsAllowlist"/>, so no API can ever read or
/// write it; flipping it requires a container recreate plus the matching compose port mapping.
/// </summary>
public sealed class SpectatorOptions
{
    public const string SectionName = "Spectator";

    /// <summary>
    /// The local TCP port the public listener is bound to, via <c>ASPNETCORE_URLS</c> (the app
    /// itself never force-binds a port — see <c>compose.yaml</c>'s <c>api</c> service). Default 0
    /// disables public-listener isolation entirely: no port configured equals no gate, and every
    /// existing internal-port behavior is unaffected.
    /// <para>
    /// Read live, per request, via <c>IOptionsMonitor&lt;SpectatorOptions&gt;</c> by
    /// <see cref="Api.SurfaceGateMiddleware"/>: when a request's <c>Connection.LocalPort</c>
    /// equals this value, only endpoints carrying <see cref="Api.SpectatorSurfaceAttribute"/> and
    /// <c>/health</c> respond — everything else (admin, <c>/media/*</c>, <c>/internal/*</c>)
    /// returns a bare 404, before authentication ever runs, regardless of any other flag
    /// (including <c>Admin:Enabled</c> and <c>Station:SpectatorMode</c>). Spectator endpoints stay
    /// reachable on the internal port too — this only ever narrows what the PUBLIC port serves.
    /// </para>
    /// </summary>
    public int PublicPort { get; init; }
}
