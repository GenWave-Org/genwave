using System.Buffers.Binary;
using System.Text;
using GenWave.VoiceOff.Pairing;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Pairing;

public sealed class CanonicalWavTests
{
    private static byte[] WithTaggedList(int dataBytes, uint? declared = null) => WavBytes.Make(8000, dataBytes, declared, extraChunk: "LIST");

    public sealed class ScenarioAWavWithAListChunk
    {
        private readonly byte[] output;

        public ScenarioAWavWithAListChunk() =>
            output = CanonicalWav.Rewrite(WithTaggedList(100), out _) ?? throw new InvalidOperationException("The fixture wav was refused.");

        [Fact]
        public void ComesOutWithNoListBytes() => Assert.DoesNotContain("LIST", Encoding.ASCII.GetString(output), StringComparison.Ordinal);

        [Fact]
        public void IsA44ByteHeaderPlusTheData() => Assert.Equal(CanonicalWav.HeaderBytes + 100, output.Length);

        [Fact]
        public void HasTheRealDataSizeInItsHeader() => Assert.Equal(100u, BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(40, 4)));
    }

    public sealed class ScenarioThePcmBytes
    {
        private readonly byte[] pcm = [.. Enumerable.Range(0, 64).Select(i => (byte)(i * 3 + 1))];
        private readonly byte[] output;

        public ScenarioThePcmBytes()
        {
            byte[] source = WithTaggedList(0);
            byte[] withData = [.. source, .. pcm];
            BinaryPrimitives.WriteUInt32LittleEndian(withData.AsSpan(withData.Length - pcm.Length - 4, 4), (uint)pcm.Length);
            output = CanonicalWav.Rewrite(withData, out _) ?? throw new InvalidOperationException("The fixture wav was refused.");
        }

        [Fact]
        public void ComeOutIdentical() => Assert.Equal(pcm, output[CanonicalWav.HeaderBytes..]);
    }

    public sealed class ScenarioAStreamingDataSize
    {
        private readonly byte[] output;

        public ScenarioAStreamingDataSize() =>
            output = CanonicalWav.Rewrite(WithTaggedList(80, declared: uint.MaxValue), out _) ?? throw new InvalidOperationException("The fixture wav was refused.");

        [Fact]
        public void IsFixedToTheRealSize() => Assert.Equal(80u, BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(40, 4)));
    }

    public sealed class ScenarioAListChunkThatDeclaresFourGigabytes
    {
        private readonly byte[]? output;
        private readonly Exception? thrown;

        public ScenarioAListChunkThatDeclaresFourGigabytes()
        {
            byte[] wav = WavBytes.Make(8000, 100, extraChunk: "LIST", listDeclared: uint.MaxValue);
            thrown = Record.Exception(() => CanonicalWav.Rewrite(wav, out _));
            output = thrown is null ? CanonicalWav.Rewrite(wav, out _) : null;
        }

        [Fact]
        public void DoesNotThrow() => Assert.Null(thrown);

        [Fact]
        public void IsRefused() => Assert.Null(output);
    }

    public sealed class ScenarioNotAWav
    {
        [Fact]
        public void IsRefused() => Assert.Null(CanonicalWav.Rewrite("not a wav at all"u8, out _));
    }
}
