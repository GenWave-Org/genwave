using System.Globalization;

namespace GenWave.Host;

/// <summary>
/// T373 review LOW-1 (fixed during the same review pass): the bare "is this catalog id numeric —
/// never a <c>tts:*</c>-prefixed synthetic one" parse, shared by <see cref="Playout.MusicAiring"/>
/// and <see cref="Engine.MediaExistencePushGuard"/>. Homed in the root <c>GenWave.Host</c> namespace
/// — <c>SingleStation</c>'s own precedent for a leaf, cross-cutting Host-level fact any layer may
/// depend on — rather than on <c>Playout.MusicAiring</c> itself: <c>Playout</c> already depends on
/// <c>Engine</c> (<c>PlayoutServiceCollectionExtensions</c> composes <c>LiquidsoapControl</c>/
/// <c>MediaExistencePushGuard</c>/<c>ArtworkUrlResolver</c>), so <c>Engine</c> calling
/// <c>Playout.MusicAiring</c> directly would close a genuine L10 namespace cycle (caught by
/// <c>GenWave.Architecture.Tests</c>'s namespace-cycle-freedom law the first time that shape was
/// tried). <see cref="Playout.MusicAiring.IsMusicMediaId"/>/<see cref="Playout.MusicAiring.TryReadMusicMediaId"/>
/// keep their own public surface — every existing Playout caller is untouched — and simply delegate
/// here now.
/// </summary>
internal static class MusicMediaId
{
    public static bool TryParse(string? mediaId, out long id) =>
        long.TryParse(mediaId, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
}
