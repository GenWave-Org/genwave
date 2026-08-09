namespace GenWave.Core.Abstractions;

/// <summary>
/// The narrow seam a disk-caching <c>GenWave.Context</c> provider (SPEC F109.2, e.g.
/// <c>HistoryContextProvider</c>) reads its writable cache directory root through. Mirrors
/// <see cref="IStationLocationProvider"/> one concern over: that seam answers "where is the station",
/// this one answers "where may this provider persist files" — kept separate for the same reason
/// (<see cref="IStationLocationProvider"/>'s own remarks), a provider that needs disk caching is a
/// minority (history today).
///
/// <para>
/// Unlike <see cref="IStationLocationProvider"/>'s <c>Station:Location:*</c>, this value is
/// deployment topology, not an operator-editable setting — the same class of value
/// <c>GenWave.Tts.TtsOptions.CacheRoot</c>/<c>ArtworkOptions.CacheDir</c> already are one layer out
/// (see those types' own remarks). <see cref="Root"/> is still read fresh on every call, matching
/// every sibling provider seam's discipline, purely so a future host implementation never has to
/// special-case this one property — in practice it never changes without a container recreate.
/// </para>
///
/// <para>
/// The Host's real binding (wrapping whichever options section owns the writable cache volume — see
/// <c>HistoryContextProvider</c>'s own remarks for exactly which) lands at PLAN T226; until then,
/// <see cref="NoOpContextCacheRootProvider"/> keeps every disk-caching provider — and every test built
/// against it — compiling and inert. A blank <see cref="Root"/> is deliberately fail-closed
/// (<see cref="ISelfGatingContextProvider"/>'s "misconfigured" edge), never a caller-visible crash: an
/// unwired cache root means "this provider has nothing to persist to yet", exactly like a blank
/// <see cref="IStationLocationProvider.Current"/> means "no location configured yet".
/// </para>
/// </summary>
public interface IContextCacheRootProvider
{
    /// <summary>The filesystem root a provider may create its own subdirectory under, evaluated fresh
    /// on every call. Blank means "not yet wired" — never a caller-visible fault.</summary>
    string Root { get; }
}
