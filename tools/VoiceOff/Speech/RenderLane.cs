namespace GenWave.VoiceOff.Speech;

/// <summary>Which text a render was made from (SPEC F214.3): the station's plain text, or text with the engine's native cue tags.</summary>
internal enum RenderLane
{
    Plain,
    Markup,
}
