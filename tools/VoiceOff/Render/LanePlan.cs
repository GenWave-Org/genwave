using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Render;

/// <summary>Which lanes an engine is rendered in: plain always, markup only when its profile maps at least one cue to a native tag.</summary>
internal static class LanePlan
{
    internal static IReadOnlyList<RenderLane> For(EngineProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Markup.Values.Any(tag => !string.IsNullOrEmpty(tag))
            ? [RenderLane.Plain, RenderLane.Markup]
            : [RenderLane.Plain];
    }
}
