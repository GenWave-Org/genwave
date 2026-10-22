using System.Text.Json;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Tests.Timing;

public sealed class TimingLogTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "voiceoff-timing-" + Guid.NewGuid().ToString("N"));

    private string LogPath => Path.Combine(directory, "nested", "timing.jsonl");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ARecordIsOneCamelCaseJsonLineWithItsRtf()
    {
        await new TimingLog(LogPath).AppendAsync(TimingRecordTests.Record(), CancellationToken.None);
        JsonElement line = JsonDocument.Parse((await File.ReadAllLinesAsync(LogPath))[0]).RootElement;
        Assert.Equal(
            ["audioMs", "engine", "error", "lane", "line", "linear", "ok", "rssMb", "rtf", "tier", "vramMb", "wallMs"],
            line.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheTierIsWrittenAsItsLowerCaseName()
    {
        await new TimingLog(LogPath).AppendAsync(TimingRecordTests.Record(), CancellationToken.None);
        Assert.Equal("cpu", JsonDocument.Parse((await File.ReadAllLinesAsync(LogPath))[0]).RootElement.GetProperty("tier").GetString());
    }

    [Fact]
    public async Task ParentFoldersAreCreated()
    {
        await new TimingLog(LogPath).AppendAsync(TimingRecordTests.Record(), CancellationToken.None);
        Assert.True(File.Exists(LogPath));
    }

    [Fact]
    public async Task AppendingKeepsEarlierLines()
    {
        TimingLog log = new(LogPath);
        await log.AppendAsync(TimingRecordTests.Record(wallMs: 1), CancellationToken.None);
        await log.AppendAsync(TimingRecordTests.Record(wallMs: 2), CancellationToken.None);
        Assert.Equal([1d, 2d], (await log.ReadAsync(CancellationToken.None)).Select(r => r.WallMs));
    }

    [Fact]
    public async Task ARecordReadBackEqualsTheRecordWritten()
    {
        TimingLog log = new(LogPath);
        TimingRecord written = TimingRecordTests.Record() with { Error = "boom", Linear = true, VramMb = 2800 };
        await log.AppendAsync(written, CancellationToken.None);
        Assert.Equal([written], await log.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AMissingFileReadsAsEmpty() =>
        Assert.Empty(await new TimingLog(LogPath).ReadAsync(CancellationToken.None));

    [Fact]
    public async Task ATornLastLineIsDropped()
    {
        TimingLog log = new(LogPath);
        await log.AppendAsync(TimingRecordTests.Record(), CancellationToken.None);
        await File.AppendAllTextAsync(LogPath, "{\"engine\":\"alp");
        Assert.Single(await log.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ACorruptEarlierLineIsNotSilentlyIgnored()
    {
        TimingLog log = new(LogPath);
        await log.AppendAsync(TimingRecordTests.Record(), CancellationToken.None);
        await File.AppendAllTextAsync(LogPath, "{\"engine\":\"alp\n");
        await log.AppendAsync(TimingRecordTests.Record(), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDataException>(() => log.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AppendingAfterATornFragmentKeepsEveryWholeRecord()
    {
        TimingLog log = new(LogPath);
        await log.AppendAsync(TimingRecordTests.Record(wallMs: 1), CancellationToken.None);
        await File.AppendAllTextAsync(LogPath, "{\"engine\":\"alp");
        await log.AppendAsync(TimingRecordTests.Record(wallMs: 2), CancellationToken.None);
        await log.AppendAsync(TimingRecordTests.Record(wallMs: 3), CancellationToken.None);
        Assert.Equal([1d, 2d, 3d], (await log.ReadAsync(CancellationToken.None)).Select(r => r.WallMs));
    }

    [Fact]
    public async Task AppendingAfterAFragmentWithNoWholeRecordStartsTheFileOver()
    {
        TimingLog log = new(LogPath);
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath) ?? directory);
        await File.WriteAllTextAsync(LogPath, "{\"engine\":\"alp");
        await log.AppendAsync(TimingRecordTests.Record(wallMs: 5), CancellationToken.None);
        Assert.Equal([5d], (await log.ReadAsync(CancellationToken.None)).Select(r => r.WallMs));
    }

    [Fact]
    public async Task AWholeRecordMissingARequiredFieldIsNotATimingRecord()
    {
        TimingLog log = new(LogPath);
        await log.AppendAsync(TimingRecordTests.Record(), CancellationToken.None);
        string whole = (await File.ReadAllLinesAsync(LogPath))[0];
        string withoutEngine = whole.Replace("\"engine\":\"alpha\",", string.Empty, StringComparison.Ordinal);
        if (withoutEngine == whole)
        {
            throw new InvalidOperationException("The fixture did not remove the engine field.");
        }

        await File.WriteAllTextAsync(LogPath, withoutEngine + "\n" + whole + "\n");
        await Assert.ThrowsAsync<InvalidDataException>(() => log.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AnEarlierRecordWithANullRequiredFieldIsNotATimingRecord()
    {
        TimingLog log = new(LogPath);
        await log.AppendAsync(TimingRecordTests.Record(), CancellationToken.None);
        string whole = (await File.ReadAllLinesAsync(LogPath))[0];
        string nullEngine = whole.Replace("\"engine\":\"alpha\"", "\"engine\":null", StringComparison.Ordinal);
        if (nullEngine == whole)
        {
            throw new InvalidOperationException("The fixture did not null the engine field.");
        }

        await File.WriteAllTextAsync(LogPath, nullEngine + "\n" + whole + "\n");
        await Assert.ThrowsAsync<InvalidDataException>(() => log.ReadAsync(CancellationToken.None));
    }
}
