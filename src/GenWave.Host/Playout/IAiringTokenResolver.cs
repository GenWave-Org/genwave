namespace GenWave.Host.Playout;

/// <summary>
/// Read-side seam over <see cref="AiringTokenRing"/> (SPEC F149.4, F150.4, STORY-369, PLAN T358) — a
/// small interface so a write path (PLAN T366's <see cref="Api.SpectatorThumbsController"/>) can
/// resolve a caller-submitted token via DI without depending on the concrete ring or its
/// <see cref="GenWave.Core.Abstractions.IStationEventSink"/> write side.
///
/// <b>Public, not internal (T366 review — promoted from this type's original PLAN T358 shape):</b> a
/// public MVC controller's constructor cannot declare a parameter of a less-accessible type (CS0051)
/// — <see cref="AiringTokenRing"/>, the only implementation, stays internal, since the interface
/// alone is what any consumer (in or out of this assembly) is ever meant to depend on.
/// </summary>
public interface IAiringTokenResolver
{
    /// <summary>
    /// <b>Current is the last music airing's token and survives an intervening non-music item; the
    /// snapshot suppresses it for non-music</b> (SPEC F150.4's grace, PLAN T358 review MED-1): an
    /// ident/patter/crosstalk/announcement airing between two tracks never clears this property —
    /// it keeps naming the last real music airing right up until the NEXT one mints a fresh token.
    /// This property therefore does NOT by itself answer "is music on air right now" — read by
    /// <see cref="PlayoutFeederService"/> at snapshot-publish time (never live from a request
    /// handler, so the value it stamps is exactly the token that airing minted, with no separate
    /// read that could race a later advance), <c>PublishSnapshot</c> is the seam that additionally
    /// gates this value on the on-air item itself being music-shaped before stamping it onto
    /// <see cref="NowPlayingSnapshot.Airing"/> — see that method's own remarks.
    /// </summary>
    string? Current { get; }

    /// <summary>
    /// Resolves <paramref name="token"/> against the CURRENT airing or the immediately PREVIOUS one
    /// only (SPEC F150.4's grace across a track change) — anything older, or never minted, resolves
    /// to <see langword="false"/>. Never throws on a garbage/unknown token (SPEC F150.3's "no
    /// oracle" posture starts here): a caller treats <see langword="false"/> as "nothing to do,"
    /// never as an error.
    /// </summary>
    bool TryResolve(string token, out long mediaId, out DateTimeOffset startedAt);
}
