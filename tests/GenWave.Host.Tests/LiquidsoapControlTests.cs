using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Domain;
using GenWave.Host.Engine;
using GenWave.Host.Options;

namespace GenWave.Host.Tests;

/// <summary>
/// §0 BLOCKING acceptance, hermetic side (PRD §12, "no queue-listing for on-air"): drives the real
/// <see cref="LiquidsoapControl"/> against a loopback fake engine and asserts the on-air read is the
/// OUTPUT metadata command and nothing else, and that our exported fields round-trip out of it.
/// </summary>
public class LiquidsoapControlTests
{
    // A realistic two-frame output.<id>.metadata reply: frame 1 is the current track and carries our
    // exported track_id/pos/on_air; frame 2 is the previous track. (Captured from Liquidsoap 2.4.4.)
    const string RealTrackReply =
        """
        --- 2 ---
        on_air="2026/06/09 02:12:26"
        on_air_timestamp="1780971146.29"
        --- 1 ---
        album="GB City"
        artist="Bass Drum of Death"
        genre="Lo-Fi"
        title="probe"
        track_id="12345"
        on_air="2026/06/09 02:12:45"
        on_air_timestamp="1780971165.54"
        """;

    // The safe rotation airing: current frame carries NO stamped track_id.
    const string SafeRotationReply =
        """
        --- 1 ---
        title="back_soon_loop"
        on_air="2026/06/09 02:20:00"
        on_air_timestamp="1780971600.0"
        """;

    // Station:PublicBaseUrl defaults empty (STORY-223, PLAN T85) — every fact in this file exercises
    // the pre-F88 push shape unless a test overrides it, so ArtworkUrlResolver never touches
    // FakeArtworkTokenStore here.
    static LiquidsoapControl Control(FakeEngineServer server) =>
        new(new LiquidsoapOptions
            {
                Host = "127.0.0.1",
                Port = server.Port,
                OutputMetadataCommand = "output.icecast.metadata",
            },
            stationId: "st-01",
            new FakeStationIdentityProvider(new StationIdentity("st-01", "GenWave", "af_heart")),
            new ArtworkUrlResolver(new FakeOptionsMonitor<StationOptions>(new StationOptions()), new FakeArtworkTokenStore()),
            NullLogger<LiquidsoapControl>.Instance);

    [Fact]
    public async Task OnAir_RealTrack_ReturnsStampedTrackId()
    {
        await using var server = new FakeEngineServer(_ => RealTrackReply);
        var ls = Control(server);

        Assert.Equal("12345", await ls.OnAirNewestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task OnAir_SafeRotation_NoStampedId_ReturnsDrainToken()
    {
        await using var server = new FakeEngineServer(_ => SafeRotationReply);
        var ls = Control(server);

        Assert.Equal(LiquidsoapControl.DrainToken, await ls.OnAirNewestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task OnAir_NothingResolved_ReturnsNull()
    {
        await using var server = new FakeEngineServer(_ => "");
        var ls = Control(server);

        Assert.Null(await ls.OnAirNewestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Metadata_ParsesCurrentFrame_ExportedFieldsRoundTrip()
    {
        await using var server = new FakeEngineServer(_ => RealTrackReply);
        var ls = Control(server);

        var meta = await ls.MetadataAsync("12345", CancellationToken.None);

        Assert.True(meta.TryGetMediaId(out var mediaId));
        Assert.Equal("12345", mediaId);                    // stamped media id round-tripped
        Assert.Equal("12345", meta.Values["track_id"]);
        Assert.True(meta.Values.ContainsKey("on_air"));    // on_air exported per PRD §0 req 5
        Assert.Equal("probe", meta.Values["title"]);       // current frame, not the previous one
    }

    [Fact]
    public async Task OnAirRead_IssuesOnlyOutputMetadataCommand_NeverQueueListing()
    {
        await using var server = new FakeEngineServer(_ => RealTrackReply);
        var ls = Control(server);

        await ls.OnAirNewestAsync(CancellationToken.None);
        await ls.MetadataAsync("12345", CancellationToken.None);

        Assert.NotEmpty(server.Commands);
        Assert.All(server.Commands, c => Assert.Equal("output.icecast.metadata", c));
        // The defects in PRD §0 came from these; on-air determination must never touch them.
        Assert.DoesNotContain(server.Commands, c => c.Contains("request.all"));
        Assert.DoesNotContain(server.Commands, c => c.Contains("request.on_air"));
        Assert.DoesNotContain(server.Commands, c => c.Contains(".queue"));
    }

    [Fact]
    public async Task Push_NonNumericReply_ThrowsInvalidOperationException()
    {
        // A rejected push returns a non-numeric reply (e.g. "ERROR" or empty). PushAsync must throw
        // so the feeder's try/catch logs a visible error and the drain-retry recovers next tick
        // rather than silently believing the track was queued (the live deadlock scenario).
        await using var server = new FakeEngineServer(_ => "ERROR");
        var ls = Control(server);
        var item = new MediaItem("1", "/media/1.mp3", "Title", new GenWave.Core.Domain.Loudness(-16.0, -1.0, Measurable: true));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ls.PushAsync(item, 0.0, CancellationToken.None));
    }

    [Fact]
    public async Task Push_StampsStationIdAndStationName()
    {
        // Issue gitea-#148: every pushed track carries the station identity so the exported output
        // metadata (and any player reading it) can display which station produced it.
        await using var server = new FakeEngineServer(_ => "42");   // valid numeric RID
        var ls = Control(server);
        var item = new MediaItem("3", "/media/3.mp3", "Title", new GenWave.Core.Domain.Loudness(-16.0, -1.0, Measurable: true));

        await ls.PushAsync(item, 0.0, CancellationToken.None);

        var command = Assert.Single(server.Commands);
        Assert.Contains("station_id=\"st-01\"", command);
        Assert.Contains("station_name=\"GenWave\"", command);
    }

    [Fact]
    public async Task Push_ArtistPresent_StampsArtist()
    {
        // Issue gitea-#148 pt 2: the engine's icy_song builds the listener-facing StreamTitle from
        // artist + title, so the catalog artist must ride the annotation rather than relying on
        // whatever ID3 tags the file happens to carry.
        await using var server = new FakeEngineServer(_ => "42");
        var ls = Control(server);
        var item = new MediaItem("3", "/media/3.mp3", "Title",
            new GenWave.Core.Domain.Loudness(-16.0, -1.0, Measurable: true), Artist: "The Band");

        await ls.PushAsync(item, 0.0, CancellationToken.None);

        Assert.Contains("artist=\"The Band\"", Assert.Single(server.Commands));
    }

    [Fact]
    public async Task Push_NoArtist_StampsExplicitlyEmptyArtistField()
    {
        // F38.1 (Epic U, gitea-#199): the omit-when-empty discipline is retired for artist — an omitted
        // key let a prior track's file-embedded tag fill the gap at request resolution (the gitea-#199
        // bleed); artist="" is a present-but-empty value that blocks that fill and always wins.
        await using var server = new FakeEngineServer(_ => "42");
        var ls = Control(server);
        var item = new MediaItem("3", "/media/3.mp3", "Title",
            new GenWave.Core.Domain.Loudness(-16.0, -1.0, Measurable: true));

        await ls.PushAsync(item, 0.0, CancellationToken.None);

        Assert.Contains("artist=\"\",", Assert.Single(server.Commands));
    }

    [Fact]
    public async Task Push_TtsItem_StampsGwTtsTrue_MusicFalse()
    {
        // Issue gitea-#151: the engine's transition butt-splices any boundary touching gw_tts="true"
        // instead of crossfading a voice. Music must carry an explicit "false".
        await using var server = new FakeEngineServer(_ => "42");
        var ls = Control(server);
        var loudness = new GenWave.Core.Domain.Loudness(-16.0, -1.0, Measurable: true);

        await ls.PushAsync(new MediaItem("tts:abc", "/tts/abc.wav", "GenWave", loudness), 0.0, CancellationToken.None);
        await ls.PushAsync(new MediaItem("42", "/media/42.mp3", "Song", loudness), 0.0, CancellationToken.None);

        Assert.Contains("gw_tts=\"true\"", server.Commands[0]);
        Assert.Contains("gw_tts=\"false\"", server.Commands[1]);
    }

    [Fact]
    public async Task Push_TitleWithNewline_SendsSingleLineCommand()
    {
        // A title containing \n would split the telnet line protocol and corrupt the command framing.
        // After escaping, the push command must arrive at the engine as a single line with no raw LF.
        await using var server = new FakeEngineServer(_ => "42");   // valid numeric RID
        var ls = Control(server);
        var item = new MediaItem("2", "/media/2.mp3", "Line1\nLine2", new GenWave.Core.Domain.Loudness(-16.0, -1.0, Measurable: true));

        await ls.PushAsync(item, 0.0, CancellationToken.None);

        // The command recorded by the fake server must be exactly one line — no embedded newline.
        var command = Assert.Single(server.Commands);
        Assert.DoesNotContain('\n', command);
        Assert.DoesNotContain('\r', command);
    }
}
