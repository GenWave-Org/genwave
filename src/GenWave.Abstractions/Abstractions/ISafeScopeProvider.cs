using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// gh-#99 — the safe-content scope accessor: which libraries hold the station's functional audio
/// (the seeded safe loop, authored safe segments, station IDs) rather than rateable music. The
/// mirror of <see cref="IStationScopeProvider"/> for <c>Station:SafeScope:LibraryIds</c>, and the
/// same contract: implementations MUST re-evaluate <see cref="Current"/> on every read — never cache
/// the result in a field — so a live SafeScope edit is visible to the very next exclusion check.
///
/// <para>
/// Consumers use this to EXCLUDE safe-scope rows from taste surfaces (F33 votes/never-play, F84
/// taste thumbs): ranking a "Please Stand By" loop or a station ID is never meaningful, and a
/// never-play write against the safe loop could silence the never-silent fallback itself. An empty
/// safe scope excludes nothing — the pre-#99 behavior.
/// </para>
/// </summary>
public interface ISafeScopeProvider
{
    /// <summary>The station's current safe-content scope, evaluated fresh on every call.</summary>
    LibraryScope Current { get; }
}
