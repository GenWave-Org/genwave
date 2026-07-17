namespace GenWave.Host.Options;

/// <summary>
/// Connection settings for the Liquidsoap control socket (config section "Liquidsoap"). The control
/// port is never published; reachability is over the internal Docker network only (PRD §9).
/// </summary>
public sealed class LiquidsoapOptions
{
    public const string Section = "Liquidsoap";

    public string Host { get; set; } = "engine";
    public int Port { get; set; } = 1234;

    /// <summary>The request.queue id; must match id="q" in genwave.liq (PRD §10).</summary>
    public string QueueId { get; set; } = "q";

    /// <summary>
    /// The output's metadata telnet command — the single source for "what is on air" (PRD §0). The
    /// output is <c>output.icecast(...)</c> in genwave.liq, so Liquidsoap names the command
    /// <c>output.icecast.metadata</c>. Reading the output (not request-level state) is the change the
    /// 2.4 engine maintainers endorse; it must carry our stamped <c>track_id</c>/<c>on_air</c>, which
    /// genwave.liq exports via <c>settings.encoder.metadata.export</c>.
    /// </summary>
    public string OutputMetadataCommand { get; set; } = "output.icecast.metadata";
}
