namespace GenWave.VoiceOff.Speech;

/// <summary>The container gate's verdict and, when it passed, the endpoint the engine answered on.</summary>
internal sealed record ContainerGateResult(GateResult Gate, Uri? Endpoint);
