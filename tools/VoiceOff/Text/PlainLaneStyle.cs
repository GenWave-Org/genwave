namespace GenWave.VoiceOff.Text;

/// <summary>How the plain lane finishes a normalized line.</summary>
internal enum PlainLaneStyle
{
    /// <summary>Normalized text only.</summary>
    Bare,

    /// <summary>Normalized text through the station's shipped speech markup, so it renders what airs today.</summary>
    StationSpeechMarkup,
}
