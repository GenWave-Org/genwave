using Microsoft.Extensions.Options;

namespace GenWave.Host.Options;

/// <summary>
/// The fail-closed read side of <see cref="CommunityOptions"/> (SPEC F90.1, STORY-234, PLAN T99):
/// wraps <see cref="IOptionsMonitor{TOptions}"/> so T101's catalog endpoints (and any UI-facing
/// surface a later task adds) read the SAME live <c>Community:CatalogIndexUrl</c> value a PUT to
/// the settings API writes — mirrors <see cref="OptionsMonitorRequestOverrideEnvelopeProvider"/>'s
/// "no restart" shape. Nothing is cached here beyond what <see cref="IOptionsMonitor{T}"/> already
/// caches, so a live edit governs the very next call.
///
/// An empty <see cref="CommunityOptions.CatalogIndexUrl"/> is the F90.1 kill switch: <see
/// cref="IsEnabled"/> is <see langword="false"/>, and <see cref="IndexUrl"/> is <see
/// langword="null"/> — T101 wires this into a bare 404 on both catalog endpoints (the same
/// F87.2/F61 surface-off idiom), and the eventual admin UI hides the Persona Catalog entry point
/// on the same signal.
/// </summary>
public sealed class CommunityCatalogAccessor(IOptionsMonitor<CommunityOptions> communityOptions)
{
    /// <summary>
    /// The configured index URL, or <see langword="null"/> when the surface is disabled — T100's
    /// <c>CatalogProxyService</c> reads this to know where to fetch from, never falling back to a
    /// stale/cached URL once the operator blanks it. A single <see cref="IOptionsMonitor{T}.CurrentValue"/>
    /// read (not one per property, the house <c>OptionsMonitor*Provider</c> idiom) — <see
    /// cref="IsEnabled"/> is derived from THIS value, so a live PUT landing between two separate
    /// reads can never produce the contradictory IsEnabled=true/IndexUrl=null (or vice versa) pair.
    /// </summary>
    public string? IndexUrl
    {
        get
        {
            var url = communityOptions.CurrentValue.CatalogIndexUrl;
            return string.IsNullOrWhiteSpace(url) ? null : url;
        }
    }

    /// <summary>
    /// <see langword="true"/> when the Persona Catalog surface is configured and should exist at
    /// all (SPEC F90.1). <see langword="false"/> is the fail-closed state a disabled/blank
    /// <c>Community:CatalogIndexUrl</c> produces — always consistent with <see cref="IndexUrl"/>,
    /// since it is derived from that same single read rather than re-reading
    /// <see cref="IOptionsMonitor{T}.CurrentValue"/> a second time.
    /// </summary>
    public bool IsEnabled => IndexUrl is not null;
}
