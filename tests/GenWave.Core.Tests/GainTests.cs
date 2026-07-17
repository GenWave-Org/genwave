using GenWave.Core.Domain;
using GenWave.Core.Playout;

namespace GenWave.Core.Tests;

public class GainTests
{
    [Fact]
    public void NormGain_SilentTrack_ReturnsZero()
    {
        // Gated/silent file: measurable=false ⇒ never auto-amplified, even though it reads ≈ −70 LUFS.
        // Applying target − (−70) would be +54 dB and detonate (PRD §4.1).
        var silent = new Loudness(IntegratedLufs: -70.0, TruePeakDbtp: double.NegativeInfinity, Measurable: false);

        Assert.Equal(0.0, Gain.NormGainDb(silent));
    }

    [Fact]
    public void NormGain_LoudPeak_ClampedByCeiling()
    {
        // Quiet program (−20 LUFS "wants" +4 dB to reach −16) but a hot true peak (+0.5 dBTP) leaves
        // only −1.5 dB of headroom under the −1 dBTP ceiling. Gain is the smaller of the two: −1.5,
        // not +4 — the track stays below target rather than clip.
        var hotPeak = new Loudness(IntegratedLufs: -20.0, TruePeakDbtp: 0.5, Measurable: true);

        Assert.Equal(-1.5, Gain.NormGainDb(hotPeak, targetLufs: -16.0, ceilingDbtp: -1.0), precision: 6);
    }
}
