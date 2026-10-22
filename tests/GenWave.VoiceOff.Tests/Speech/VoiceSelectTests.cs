using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Script;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Speech;

public sealed class VoiceSelectTests
{
    private static AudioFingerprint Fp(params double[] values) => new(values);

    private static EngineProfile Profile(string bubbly, string laconic, string lateNight) =>
        EngineProfileReader.Parse(
            EngineProfileJson.With(n =>
            {
                var voices = EngineProfileJson.Child(n, "voices");
                voices["bubbly"] = bubbly;
                voices["laconic"] = laconic;
                voices["late-night"] = lateNight;
            }), "alpha.json");

    [Fact]
    public void DistanceIsEuclidean() => Assert.Equal(5.0, AudioFingerprint.Distance(Fp(0, 0), Fp(3, 4)));

    [Fact]
    public void ExactlyThreeTimesPasses() =>
        Assert.True(VoiceSelectGate.Evaluate(Fp(0), Fp(1), Fp(3)).Passed);

    [Fact]
    public void JustUnderThreeTimesFails() =>
        Assert.False(VoiceSelectGate.Evaluate(Fp(0), Fp(1), Fp(2.9)).Passed);

    [Fact]
    public void ADeterministicEngineNeedsOnlyAnyDifference() =>
        Assert.True(VoiceSelectGate.Evaluate(Fp(1), Fp(1), Fp(1.5)).Passed);

    [Fact]
    public void IdenticalAudioFailsWithAReason() =>
        Assert.Contains("sound the same", VoiceSelectGate.Evaluate(Fp(1), Fp(1), Fp(1)).Reason, StringComparison.Ordinal);

    [Fact]
    public void BubblyAndLateNightAreComparedFirst() =>
        Assert.Equal((GauntletRegister.Bubbly, GauntletRegister.LateNight), VoiceSelectGate.PickRegisters(Profile("a", "b", "c")));

    [Fact]
    public void WhenTheExtremesShareAVoiceTheNextPairIsUsed() =>
        Assert.Equal((GauntletRegister.Bubbly, GauntletRegister.Laconic), VoiceSelectGate.PickRegisters(Profile("a", "b", "a")));

    [Fact]
    public void OneVoiceEverywhereHasNothingToCompare() =>
        Assert.Null(VoiceSelectGate.PickRegisters(Profile("a", "a", "a")));

    [Fact]
    public void OneVoiceFailsWithThatReason() => Assert.Equal("one voice", VoiceSelectGate.OneVoice().Reason);

    private static readonly string[] AllStats =
    [
        "lavfi.astats.1.RMS_level=-20.5",
        "lavfi.astats.1.Peak_level=-3",
        "lavfi.astats.1.Zero_crossings_rate=0.05",
        "lavfi.astats.1.Entropy=0.9",
    ];

    [Fact]
    public void AReportWithEveryFeatureHasFour() =>
        Assert.Equal(4, AudioFingerprint.Parse(AllStats, "x.wav").Features.Count);

    [Fact]
    public void AMissingFeatureIsAFailureNamingIt()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => AudioFingerprint.Parse(AllStats.Where(l => !l.Contains("Zero_crossings_rate", StringComparison.Ordinal)), "x.wav"));
        Assert.Contains("Zero_crossings_rate", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MinusInfIsTheSilenceFloor() =>
        Assert.Equal(-120.0 / 10.0, AudioFingerprint.Parse(AllStats.Select(l => l.Replace("-20.5", "-inf", StringComparison.Ordinal)), "x.wav").Features[0]);

    [Fact]
    public void AnUnreadableValueIsAFailureNotSilence() =>
        Assert.Throws<InvalidOperationException>(
            () => AudioFingerprint.Parse(AllStats.Select(l => l.Replace("-20.5", "nan", StringComparison.Ordinal)), "x.wav"));

    [Fact]
    public async Task TonesAnOctaveApartPassAndTheSameToneTwiceDoesNot()
    {
        string dir = Path.Combine(Path.GetTempPath(), "voiceoff-tones-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            AudioFingerprint a1 = await Tone(dir, "a1", 440);
            AudioFingerprint a2 = await Tone(dir, "a2", 440);
            AudioFingerprint b = await Tone(dir, "b", 880);
            Assert.Equal(
                (true, false),
                (VoiceSelectGate.Evaluate(a1, a2, b).Passed, VoiceSelectGate.Evaluate(a1, a2, a2).Passed));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task<AudioFingerprint> Tone(string dir, string name, int hertz)
    {
        string path = Path.Combine(dir, name + ".wav");
        await GenWave.Loudness.FfmpegProcess.RunFfmpegAsync(
            ["-hide_banner", "-v", "error", "-f", "lavfi", "-i", $"sine=frequency={hertz}:duration=1:sample_rate=24000", "-y", path],
            CancellationToken.None);
        return await AudioFingerprint.FromFileAsync(path, CancellationToken.None);
    }
}
