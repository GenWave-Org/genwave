// STORY-557 — Upload a clip, get a voice (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. The T756 facts drive the REAL POST/GET/DELETE api/voices/clips routes through
// WebApplicationFactory<Program> against a REAL ephemeral Postgres and real ffmpeg (the Story401 arc idiom: arrange once,
// many read-only Scenarios). Test audio is made at run time with ffmpeg's sine source. The T757 facts stay pending.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;
using GenWave.Loudness;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace GenWave.Host.Tests.Specs;

public static class FeatureUploadAClip
{
    // AC1–AC3 — a 10 s WAV and a label posted to api/voices/clips
    [Collection(UploadAClipCollection.Name)]
    public sealed class ScenarioAGoodUpload(UploadAClipArc arc)
    {
        [Fact]
        public void ARowExists() => Assert.True(arc.GoodRowExists);

        [Fact]
        public void TheStoredFileIsMono() => Assert.Equal(1, arc.GoodFile.Channels);

        [Fact]
        public void TheStoredFileIs24Kilohertz() => Assert.Equal(24000, arc.GoodFile.SampleRate);

        // Measured on the stored file itself with a tolerance of 0.5 LU (the limiter must not shift the level).
        [Fact]
        public void TheStoredFileIsAtMinus20Lufs() => Assert.InRange(arc.GoodFile.IntegratedLufs, -20.5, -19.5);

        // The listing is proved through this GET; the engine-side listing of the folder is proved at T770/T771.
        [Fact]
        public void TheClipIsListed() => Assert.Contains(UploadAClipArc.GoodSlug, arc.ListedSlugs);
    }

    // AC4
    [Collection(UploadAClipCollection.Name)]
    public sealed class ScenarioDeletingAFreeClip(UploadAClipArc arc)
    {
        [Fact]
        public void TheRowIsGone() => Assert.False(arc.FreeRowSurvivesDelete);

        [Fact]
        public void TheFileIsGone() => Assert.False(arc.FreeFileSurvivesDelete);
    }

    // AC5, AC11 — a fresh install
    public sealed class ScenarioTheStarterSet
    {
        [Fact(Skip = "pending: T757")]
        public void StarterClipsExist() { }

        [Fact(Skip = "pending: T757")]
        public void EveryStarterClipHasACredit() { }

        [Fact(Skip = "pending: T757")]
        public void NoCreditNamesANonCommercialLicence() { }
    }

    // Sad path
    // AC7, AC9, AC10
    [Collection(UploadAClipCollection.Name)]
    public sealed class ScenarioRefusedUploads(UploadAClipArc arc)
    {
        [Fact]
        public void AThreeSecondClipReturns400WithTheReason() =>
            Assert.Equal((HttpStatusCode.BadRequest, true), (arc.ShortStatus, arc.ShortBodyNamesTheReason));

        [Fact]
        public void AnOversizeFileReturns400() => Assert.Equal(HttpStatusCode.BadRequest, arc.OversizeStatus);

        [Fact]
        public void AnAnonymousUploadReturns401() => Assert.Equal(HttpStatusCode.Unauthorized, arc.AnonymousUploadStatus);

        [Fact]
        public void AnAnonymousDeleteReturns401() => Assert.Equal(HttpStatusCode.Unauthorized, arc.AnonymousDeleteStatus);

        [Fact]
        public void AHostileLabelIsStoredUnderASlugInsideTheClipsFolder() => Assert.True(arc.HostileLabelStayedInTheClipsFolder);
    }

    // Edge — a row whose file is missing
    [Collection(UploadAClipCollection.Name)]
    public sealed class ScenarioARowWithoutAFile(UploadAClipArc arc)
    {
        [Fact]
        public void TheSameLabelIsStoredUnderTheNextSuffix() => Assert.Equal("ghost-2", arc.GhostUploadSlug);
    }

    // Sad path
    // AC8 — a DJ's voice map names the clip
    [Collection(UploadAClipCollection.Name)]
    public sealed class ScenarioDeletingAClipInUse(UploadAClipArc arc)
    {
        [Fact]
        public void TheAnswerIs409() => Assert.Equal(HttpStatusCode.Conflict, arc.InUseStatus);

        [Fact]
        public void TheAnswerNamesTheDj() => Assert.Contains(UploadAClipArc.DjName, arc.InUseBody, StringComparison.Ordinal);
    }
}

// ── The arc — one real Postgres, one running app, real ffmpeg ──────────────────────────────────────

[CollectionDefinition(Name)]
public sealed class UploadAClipCollection : ICollectionFixture<UploadAClipArc>
{
    public const string Name = "Story557UploadAClip";
}

/// <summary>What ffprobe and ebur128 say about a stored clip file.</summary>
public sealed record StoredClipFacts(int Channels, int SampleRate, double IntegratedLufs);

/// <summary>
/// Arranges every T756 fact: boots ONE ephemeral Postgres and ONE hosted-service-free app with a scratch clips folder, then uploads,
/// lists, deletes and refuses through the real routes. The in-use case imports a DJ card whose voice map names the clip on chatterbox.
/// </summary>
public sealed class UploadAClipArc : IAsyncLifetime
{
    public const string GoodSlug = "my-reader";
    public const string DjName = "DJ Clipper";

    public bool GoodRowExists { get; private set; }
    public StoredClipFacts GoodFile { get; private set; } = new(0, 0, 0);
    public IReadOnlyList<string> ListedSlugs { get; private set; } = [];

    public bool FreeRowSurvivesDelete { get; private set; } = true;
    public bool FreeFileSurvivesDelete { get; private set; } = true;

    public HttpStatusCode ShortStatus { get; private set; }
    public bool ShortBodyNamesTheReason { get; private set; }
    public HttpStatusCode OversizeStatus { get; private set; }
    public HttpStatusCode AnonymousUploadStatus { get; private set; }
    public HttpStatusCode AnonymousDeleteStatus { get; private set; }
    public bool HostileLabelStayedInTheClipsFolder { get; private set; }

    public string GhostUploadSlug { get; private set; } = "";
    public HttpStatusCode InUseStatus { get; private set; }
    public string InUseBody { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await using var database = await UploadAClipDatabase.StartAsync();
        using var clipsDir = new TempDir();
        using var audioDir = new TempDir();
        var clipsRoot = Path.Combine(clipsDir.Path, "voice-clips");
        await using var factory = new UploadAClipWebFactory(database, clipsRoot);
        var admin = await UploadAClipWebFactory.LoggedInClientAsync(factory);

        var tenSeconds = await MakeWavAsync(audioDir.Path, "ten.wav", seconds: 10);

        // AC1–AC3 — a good upload
        var good = await UploadAsync(admin, "My Reader", tenSeconds);
        Assert.True(good.StatusCode == HttpStatusCode.Created, await good.Content.ReadAsStringAsync());
        GoodRowExists = await CountRowsAsync(database.StationConnectionString, GoodSlug) == 1;
        GoodFile = await MeasureAsync(Path.Combine(clipsRoot, GoodSlug + ".wav"));
        ListedSlugs = await ListSlugsAsync(admin);

        // AC4 — deleting a free clip
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(admin, "Free One", tenSeconds)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/voices/clips/free-one")).StatusCode);
        FreeRowSurvivesDelete = await CountRowsAsync(database.StationConnectionString, "free-one") > 0;
        FreeFileSurvivesDelete = File.Exists(Path.Combine(clipsRoot, "free-one.wav"));

        // AC7 — a 3 s clip
        var threeSeconds = await MakeWavAsync(audioDir.Path, "three.wav", seconds: 3);
        var tooShort = await UploadAsync(admin, "Too Short", threeSeconds);
        ShortStatus = tooShort.StatusCode;
        ShortBodyNamesTheReason = (await tooShort.Content.ReadAsStringAsync()).Contains("shorter than 5 seconds", StringComparison.Ordinal);

        // AC7 — more than 10 MiB (a WAV header then zeros; the size gate runs before the audio is read)
        var oversize = Path.Combine(audioDir.Path, "big.wav");
        await File.WriteAllBytesAsync(oversize, [.. "RIFF\0\0\0\0WAVE"u8, .. new byte[10 * 1024 * 1024 + 1]]);
        OversizeStatus = (await UploadAsync(admin, "Too Big", oversize)).StatusCode;

        // AC9 — nobody without a session can upload or delete
        var anonymous = factory.CreateClient();
        AnonymousUploadStatus = (await UploadAsync(anonymous, "Sneaky", tenSeconds)).StatusCode;
        AnonymousDeleteStatus = (await anonymous.DeleteAsync($"/api/voices/clips/{GoodSlug}")).StatusCode;

        // AC10 — a hostile label becomes a slug and nothing lands outside the clips folder
        var hostile = await UploadAsync(admin, "../x.wav", tenSeconds);
        HostileLabelStayedInTheClipsFolder = hostile.StatusCode == HttpStatusCode.Created
            && await CountRowsAsync(database.StationConnectionString, "x-wav") == 1
            && File.Exists(Path.Combine(clipsRoot, "x-wav.wav"))
            && !File.Exists(Path.Combine(clipsDir.Path, "x.wav"))
            && !File.Exists(Path.Combine(clipsRoot, "x.wav"));

        // Edge — a row exists but its file does not
        await using (var conn = new NpgsqlConnection(database.StationConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "insert into station.voice_clip (slug, label, file_name, duration_ms, source) values ('ghost', 'Ghost', 'ghost.wav', 10000, 'upload')");
        }
        var ghost = await UploadAsync(admin, "Ghost", tenSeconds);
        using (var ghostBody = JsonDocument.Parse(await ghost.Content.ReadAsStringAsync()))
            GhostUploadSlug = ghostBody.RootElement.TryGetProperty("slug", out var ghostSlug) ? ghostSlug.GetString() ?? "" : "";

        // AC8 — a DJ's voice map names the clip
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(admin, "Used Voice", tenSeconds)).StatusCode);
        var import = await admin.PostAsync(
            "/api/personas/dj-clipper/import", new StringContent(DjCardJson("used-voice"), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, import.StatusCode);
        var inUse = await admin.DeleteAsync("/api/voices/clips/used-voice");
        InUseStatus = inUse.StatusCode;
        InUseBody = await inUse.Content.ReadAsStringAsync();
    }

    static string DjCardJson(string clipSlug) =>
        "{\"schemaVersion\":1,\"name\":\"" + DjName + "\",\"tagline\":\"\",\"soul\":\"\",\"quirks\":[]," +
        "\"voice\":{\"engine\":\"kokoro\",\"voiceId\":\"af_heart\",\"pace\":1.0,\"language\":\"en\"}," +
        "\"energyDisposition\":0.4,\"lore\":[],\"corrections\":[]," +
        $"\"voices\":{{\"kokoro\":\"af_heart\",\"chatterbox\":\"{clipSlug}\"}}}}";

    static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string label, string path)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(label), "label");
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(path));
        form.Add(file, "file", Path.GetFileName(path));
        return await client.PostAsync("/api/voices/clips", form);
    }

    static async Task<IReadOnlyList<string>> ListSlugsAsync(HttpClient client)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/voices/clips"));
        return [.. body.RootElement.EnumerateArray().Select(c => c.GetProperty("slug").GetString() ?? "")];
    }

    static async Task<int> CountRowsAsync(string stationConnectionString, string slug)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>("select count(*)::int from station.voice_clip where slug = @slug", new { slug });
    }

    /// <summary>A stereo 44.1 kHz sine of <paramref name="seconds"/> seconds, made by ffmpeg.</summary>
    static async Task<string> MakeWavAsync(string directory, string name, int seconds)
    {
        var path = Path.Combine(directory, name);
        var result = await FfmpegProcess.RunFfmpegCapturingStderrAsync(
            ["-nostdin", "-v", "error", "-f", "lavfi", "-i",
             $"sine=frequency=440:sample_rate=44100:duration={seconds.ToString(CultureInfo.InvariantCulture)}",
             "-ac", "2", "-y", path], CancellationToken.None);
        Assert.True(result.Succeeded, result.Stderr);
        return path;
    }

    static async Task<StoredClipFacts> MeasureAsync(string path)
    {
        var psi = new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var arg in new[] { "-v", "error", "-select_streams", "a:0", "-show_entries", "stream=channels,sample_rate", "-of", "csv=p=0", path })
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("ffprobe did not start");
        var output = (await process.StandardOutput.ReadToEndAsync()).Trim().Split(',');
        await process.WaitForExitAsync();

        var loudness = await new FfmpegLoudnessAnalyzer().AnalyzeAsync(path, CancellationToken.None);
        return new StoredClipFacts(
            int.Parse(output[1], CultureInfo.InvariantCulture), int.Parse(output[0], CultureInfo.InvariantCulture), loudness.IntegratedLufs);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

file sealed class UploadAClipDatabase : EphemeralStationDatabase
{
    UploadAClipDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<UploadAClipDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t756-557");
        var db = new UploadAClipDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>The real Program.cs graph against the ephemeral Postgres, no hosted services, chatterbox configured, Kokoro's voice list stubbed.</summary>
file sealed class UploadAClipWebFactory(UploadAClipDatabase database, string clipsRoot) : WebApplicationFactory<Program>
{
    const string Password = "test-password-story557-upload-a-clip";

    readonly FakeHttpMessageHandler handler = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new { voices = new[] { "af_heart" } }),
    }));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", database.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", database.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");
        builder.UseSetting("Tts:ClipsRoot", clipsRoot);
        builder.UseSetting("Tts:Engines:chatterbox:Endpoint", "http://chatterbox:8004");
        builder.UseSetting("Tts:Engines:chatterbox:Profile", "chatterbox");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(handler));
        });
    }

    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}
