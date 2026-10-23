using GenWave.VoiceOff.Audio;

namespace GenWave.VoiceOff.Tests.Audio;

public sealed class ConcatArgsTests
{
    private static string FilterOf(IReadOnlyList<string> args) => args[args.ToList().IndexOf("-filter_complex") + 1];

    [Fact]
    public void TwoTurnsAreJoinedWithOneSilenceBetween()
    {
        IReadOnlyList<string> args = ConcatArgs.Build(["/tmp/t1.wav", "/tmp/t2.wav"], 0.6, "/tmp/out.wav");
        Assert.Equal(
            [
                "-nostdin", "-hide_banner", "-y",
                "-i", "file:/tmp/t1.wav",
                "-f", "lavfi", "-t", "0.6", "-i", "anullsrc=r=24000:cl=mono",
                "-i", "file:/tmp/t2.wav",
                "-filter_complex",
                "[0:a]aformat=sample_rates=24000:channel_layouts=mono:sample_fmts=s16[t0];[1:a]aformat=sample_rates=24000:channel_layouts=mono:sample_fmts=s16[s1];[2:a]aformat=sample_rates=24000:channel_layouts=mono:sample_fmts=s16[t1];[t0][s1][t1]concat=n=3:v=0:a=1[out]",
                "-map", "[out]", "-c:a", "pcm_s16le", "-f", "wav", "/tmp/out.wav",
            ],
            args);
    }

    [Fact]
    public void ThreeTurnsHaveTwoSilences() =>
        Assert.EndsWith("[t0][s1][t1][s2][t2]concat=n=5:v=0:a=1[out]", FilterOf(ConcatArgs.Build(["/a.wav", "/b.wav", "/c.wav"], 0.6, "/o.wav")), StringComparison.Ordinal);

    [Fact]
    public void ADashLeadingNameIsAFileNotAnOption()
    {
        IReadOnlyList<string> args = ConcatArgs.Build(["-evil.wav", "/b.wav"], 0.6, "/o.wav");
        Assert.Contains($"file:{Path.GetFullPath("-evil.wav")}", args);
    }

    [Fact]
    public void NoTurnsIsRefused() => Assert.Throws<ArgumentException>(() => ConcatArgs.Build([], 0.6, "/o.wav"));
}
