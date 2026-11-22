// STORY-556 — Every speaker keeps a voice per engine, ad previews on the wire (SPEC F219.8/.9 · PLAN T753b)
//
// BDD specification — xUnit through the deployed entry point: POST /api/ads/{id}/preview on a real host
// (WebApplicationFactory<Program> against an ephemeral Postgres, the Story424 idiom) whose active engine
// is chatterbox and whose announcer, cast and station voices are engine maps. The mixer is the only fake;
// it records the cast the real AdRenderService handed it, so the fact proves which voice each speaker got.

using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Dapper;
using Npgsql;
using GenWave.Ads;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAdPreviewOnChatterbox
{
    [Collection(Story556AdPreviewCollection.Name)]
    public sealed class ScenarioThePreviewUsesTheMappedCastVoice(Story556AdPreviewArc arc)
    {
        [Fact]
        public void ThePreviewSettled() => Assert.True(arc.RenderSettled, "the preview job never settled");

        [Fact]
        public void TheAnnouncerSpeaksWithItsChatterboxVoice() => Assert.Equal("announcer-cb", arc.VoiceOf("ANNOUNCER"));

        [Fact]
        public void TheCastMemberSpeaksWithTheirChatterboxVoice() => Assert.Equal("cast-one-cb", arc.VoiceOf("VOICE1"));
    }
}

[CollectionDefinition(Name)]
public sealed class Story556AdPreviewCollection : ICollectionFixture<Story556AdPreviewArc>
{
    public const string Name = "Story556AdPreview";
}

public sealed class Story556AdPreviewArc : IAsyncLifetime
{
    public bool RenderSettled { get; private set; }

    CastAssemblyRequest? request;

    public string VoiceOf(string tag) =>
        request?.Cast.Single(member => member.Tag == tag).Voice.VoiceId ?? "";

    public async Task InitializeAsync()
    {
        await using var database = await Story556Database.StartAsync();
        using var authoredRoot = new TempDir();
        await SeedAdsLibraryAsync(database.LibraryConnectionString);

        await using var factory = new Story556WebFactory(database, authoredRoot.Path);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Story556WebFactory.Password });
        login.EnsureSuccessStatusCode();

        var sponsorId = await AdSpotJobTestHelpers.CreateSponsorAsync(client, "Larkspur & Loom Mercantile");
        var (spotId, _) = await AdSpotJobTestHelpers.CreateDraftSpotWithScriptAsync(
            client, sponsorId, "Chatterbox preview",
            "ANNOUNCER: Larkspur & Loom has a deal.\nVOICE1: It is a very good deal.");

        (await AdSpotJobTestHelpers.PostPreviewAsync(client, spotId)).EnsureSuccessStatusCode();
        var (settled, _) = await AdSpotJobTestHelpers.TryPollUntilAsync(
            client, spotId, AdSpotJobTestHelpers.PreviewJobSettled, TimeSpan.FromSeconds(10));
        RenderSettled = settled;
        request = ((WavHeaderCastSegmentAuthor)factory.Services.GetRequiredService<ICastSegmentAuthor>())
            .LastAssembleOnlyRequest;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    static async Task SeedAdsLibraryAsync(string libraryConnectionString)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteScalarAsync<long>("insert into library.library (name) values ('ads') returning id");
    }
}

file sealed class Story556WebFactory(Story556Database db, string authoredRoot) : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t753b-ad-preview";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "{\"kokoro\":\"af_heart\",\"chatterbox\":\"station-cb\"}");
        builder.UseSetting("Station:Ads:AnnouncerVoice", "{\"kokoro\":\"af_nova\",\"chatterbox\":\"announcer-cb\"}");
        builder.UseSetting("Station:Ads:CastVoices",
            "[{\"kokoro\":\"am_adam\",\"chatterbox\":\"cast-one-cb\"},{\"kokoro\":\"af_sky\",\"chatterbox\":\"cast-two-cb\"}]");
        builder.UseSetting("Tts:Engine", "chatterbox");
        builder.UseSetting("Tts:Engines:chatterbox:Endpoint", "http://chatterbox:8004");
        builder.UseSetting("Tts:Engines:chatterbox:Profile", "chatterbox");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");
        builder.UseSetting("Station:Scope:LibraryIds:1", "2");
        builder.UseSetting("Station:Safe:AuthoredRoot", authoredRoot);
        builder.UseSetting("Ads:JobQueueCapacity", "4");
        builder.UseSetting("Llm:Endpoint", "http://fake-llm.local");
        builder.UseSetting("Llm:Model", "test-model");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddHostedService(sp => sp.GetRequiredService<AdSpotJobService>());
            services.RemoveAll<ICastSegmentAuthor>();
            services.AddSingleton<ICastSegmentAuthor, WavHeaderCastSegmentAuthor>();
        });
    }
}

file sealed class Story556Database : EphemeralStationDatabase
{
    Story556Database(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story556Database> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t753b");
        var db = new Story556Database(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}
