namespace GenWave.VoiceOff.Speech;

/// <summary>A gate's verdict; a failed gate always says why.</summary>
internal sealed record GateResult(bool Passed, string Reason)
{
    internal static GateResult Pass(string reason) => new(true, reason);

    internal static GateResult Fail(string reason) => new(false, reason);
}
