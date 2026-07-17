namespace GenWave.Host;

/// <summary>
/// One deployment broadcasts exactly one station (single-station model). These constants are the
/// fixed identity used wherever a station id is still required by a kept type (play-history /
/// now-playing keys, the <c>/api/stations</c> response). A second station = a second deployment.
/// </summary>
internal static class SingleStation
{
    public const long Id = 1;
    public const string IdString = "1";
}
