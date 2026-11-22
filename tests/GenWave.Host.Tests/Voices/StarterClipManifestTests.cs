using System.Buffers.Binary;
using GenWave.Host.Voices;

namespace GenWave.Host.Tests.Voices;

/// <summary>The shipped starter set on disk (SPEC F219.14, PLAN T757): manifest, attribution and the WAV files themselves.</summary>
public sealed class StarterClipManifestTests
{
    static readonly string ClipsDir = Path.Combine(RepoRoot(), "chatterbox", "clips");

    static readonly IReadOnlyList<StarterClipEntry> Entries =
        StarterClipManifest.Parse(File.ReadAllText(Path.Combine(ClipsDir, StarterClipManifest.FileName)));

    [Fact]
    public void TheManifestHoldsThirteenValidEntriesWithDistinctSlugs() =>
        Assert.Equal(13, Entries.Select(e => e.Slug).Distinct(StringComparer.Ordinal).Count());

    [Fact]
    public void AttributionCarriesOneCreditLinePerClip()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), "chatterbox", "ATTRIBUTION.txt"));
        Assert.All(Entries, e => Assert.Contains($"{e.Slug}: {e.Credit}", lines));
    }

    [Fact]
    public void EveryClipIsMono24KilohertzAndBetween5And20Seconds() =>
        Assert.All(Entries, e =>
        {
            var (channels, rate, seconds) = ReadWavHeader(Path.Combine(ClipsDir, e.File));
            Assert.Equal((1, 24000), (channels, rate));
            Assert.InRange(seconds, 5.0, 20.0);
            Assert.InRange(Math.Abs(seconds * 1000 - e.DurationMs), 0, 5);
        });

    // The limiter's ceiling is 0.95 of full scale; a peak of zero would mean a silent file.
    [Fact]
    public void EveryClipPeaksAtOrBelowTheLimiterCeilingAndIsNotSilent() =>
        Assert.All(Entries, e => Assert.InRange(PeakSample(Path.Combine(ClipsDir, e.File)), 1, (int)(0.95 * 32768) + 1));

    static int PeakSample(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4));
            if (System.Text.Encoding.ASCII.GetString(bytes, offset, 4) == "data")
            {
                var peak = 0;
                for (var i = offset + 8; i + 1 < offset + 8 + size && i + 1 < bytes.Length; i += 2)
                    peak = Math.Max(peak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i))));
                return peak;
            }

            offset += 8 + size + (size & 1);
        }

        throw new InvalidDataException($"No data chunk in {path}");
    }

    static (int Channels, int Rate, double Seconds) ReadWavHeader(string path)
    {
        var bytes = File.ReadAllBytes(path);
        int channels = 0, rate = 0, bits = 16;
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4));
            if (id == "fmt ")
            {
                channels = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset + 10));
                rate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 12));
                bits = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset + 22));
            }
            else if (id == "data")
            {
                return (channels, rate, (double)size / (channels * (bits / 8)) / rate);
            }

            offset += 8 + size + (size & 1);
        }

        throw new InvalidDataException($"No data chunk in {path}");
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("repo root (GenWave.sln) not found");
    }
}
