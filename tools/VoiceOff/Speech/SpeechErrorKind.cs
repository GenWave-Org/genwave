namespace GenWave.VoiceOff.Speech;

/// <summary>Why a call to an engine did not give usable output.</summary>
internal enum SpeechErrorKind
{
    /// <summary>No usable answer arrived: refused, DNS, reset, or the connection dropped mid-answer.</summary>
    Unreachable,

    /// <summary>The engine answered with a non-2xx status.</summary>
    HttpStatus,

    /// <summary>The engine did not answer within the profile's timeout.</summary>
    Timeout,

    /// <summary>The engine answered 2xx with nothing usable in the body.</summary>
    EmptyBody,

    /// <summary>The engine's 2xx body was bigger than the client is willing to read.</summary>
    TooLarge,
}
