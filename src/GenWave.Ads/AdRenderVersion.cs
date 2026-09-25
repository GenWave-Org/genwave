namespace GenWave.Ads;

/// <summary>
/// The render pipeline's own version (gh-#854) — bump <see cref="Current"/> whenever render OUTPUT
/// changes (TTS normalisation, mixer, bed), and every existing <c>ready</c> spot re-renders once in
/// the background on the next boot. Pre-existing rows carry <c>render_version = 0</c> (db/48), so they
/// are stale on first boot of any release that bumps this.
/// </summary>
public static class AdRenderVersion
{
    public const int Current = 1;
}
