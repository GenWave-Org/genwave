using GenWave.VoiceOff.Script;

namespace GenWave.VoiceOff.Speech;

/// <summary>
/// The voiceSelect gate (SPEC F214.6): changing the <c>voice</c> value must change the audio. Two renders
/// with one voice (a1, a2) and one with another (b) pass when dist(a1,b) is at least
/// <see cref="Margin"/> times dist(a1,a2).
/// </summary>
internal static class VoiceSelectGate
{
    internal const double Margin = 3.0;
    internal const double Epsilon = 1e-6;

    /// <summary>The two voices to compare: the first pair of registers, widest apart first, that were given different voices.</summary>
    internal static (GauntletRegister A, GauntletRegister B)? PickRegisters(EngineProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        (GauntletRegister, GauntletRegister)[] pairs =
        [
            (GauntletRegister.Bubbly, GauntletRegister.LateNight),
            (GauntletRegister.Bubbly, GauntletRegister.Laconic),
            (GauntletRegister.Laconic, GauntletRegister.LateNight),
        ];
        foreach ((GauntletRegister a, GauntletRegister b) in pairs)
        {
            if (!string.Equals(profile.VoiceFor(a), profile.VoiceFor(b), StringComparison.Ordinal))
            {
                return (a, b);
            }
        }

        return null;
    }

    /// <summary>The verdict for a profile that gave every register the same voice, so there is nothing to compare.</summary>
    internal static GateResult OneVoice() => GateResult.Fail("one voice");

    internal static GateResult Evaluate(AudioFingerprint sameVoiceFirst, AudioFingerprint sameVoiceSecond, AudioFingerprint otherVoice)
    {
        double within = AudioFingerprint.Distance(sameVoiceFirst, sameVoiceSecond);
        double across = AudioFingerprint.Distance(sameVoiceFirst, otherVoice);
        bool passed = within <= Epsilon ? across > Epsilon : across >= Margin * within;
        string detail = $"across {across:0.####} vs within {within:0.####}";
        return passed
            ? GateResult.Pass(detail)
            : GateResult.Fail($"voices sound the same: {detail}");
    }
}
