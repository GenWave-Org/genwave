using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Speech;

public sealed class SpeechClientFailureTests
{
    private static async Task<SpeechResult> RenderAgainst(EngineStubServer stub, TimeSpan timeout)
    {
        using HttpClient http = SpeechClient.CreateHttpClient();
        return await new SpeechClient(http, stub.BaseUri, timeout).RenderAsync("hello there", "v1", 1.0, CancellationToken.None);
    }

    [Fact]
    public async Task ANon2xxCarriesTheStatusAndTheBodyHead()
    {
        await using EngineStubServer stub = await EngineStubServer.StartAsync(speechStatus: 500, speechBody: "boom"u8.ToArray());
        SpeechResult result = await RenderAgainst(stub, TimeSpan.FromSeconds(10));
        Assert.Equal((SpeechErrorKind.HttpStatus, "HTTP 500: boom"), (result.Error?.Kind, result.Error?.Message));
    }

    [Fact]
    public async Task ALongErrorBodyIsCutAt200Characters()
    {
        await using EngineStubServer stub = await EngineStubServer.StartAsync(speechStatus: 500, speechBody: new string('x', 1000).Select(c => (byte)c).ToArray());
        SpeechResult result = await RenderAgainst(stub, TimeSpan.FromSeconds(10));
        Assert.Equal("HTTP 500: ".Length + 200, result.Error?.Message.Length);
    }

    [Fact]
    public async Task AnEmptyBodyIsAnError()
    {
        await using EngineStubServer stub = await EngineStubServer.StartAsync(speechBody: []);
        SpeechResult result = await RenderAgainst(stub, TimeSpan.FromSeconds(10));
        Assert.Equal(SpeechErrorKind.EmptyBody, result.Error?.Kind);
    }

    [Fact]
    public async Task ASlowEngineTimesOut()
    {
        await using EngineStubServer stub = await EngineStubServer.StartAsync(speechDelay: TimeSpan.FromSeconds(5));
        SpeechResult result = await RenderAgainst(stub, TimeSpan.FromMilliseconds(200));
        Assert.Equal(SpeechErrorKind.Timeout, result.Error?.Kind);
    }

    [Fact]
    public async Task TheTimeoutMessageStatesTheProfilesRealTimeout()
    {
        await using EngineStubServer stub = await EngineStubServer.StartAsync(speechDelay: TimeSpan.FromSeconds(5));
        SpeechResult result = await RenderAgainst(stub, TimeSpan.FromMilliseconds(300));
        Assert.Equal("No answer within 0.3 seconds.", result.Error?.Message);
    }

    [Fact]
    public void AnHttpClientWithItsOwnTimeoutIsRefused()
    {
        using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(100) };
        Assert.Throws<ArgumentException>(() => new SpeechClient(http, new Uri("http://127.0.0.1:1"), TimeSpan.FromSeconds(120)));
    }

    [Fact]
    public async Task AProfileTimeoutLongerThan100SecondsIsTheDeadlineUsed()
    {
        await using EngineStubServer stub = await EngineStubServer.StartAsync(speechDelay: TimeSpan.FromSeconds(30));
        ManualTimeProvider time = new();
        using HttpClient http = SpeechClient.CreateHttpClient();
        Task<SpeechResult> render = new SpeechClient(http, stub.BaseUri, TimeSpan.FromSeconds(120), time).RenderAsync("hello there", "v1", 1.0, CancellationToken.None);
        time.FireAll();
        SpeechResult result = await render;
        Assert.Equal((TimeSpan.FromSeconds(120), "No answer within 120 seconds."), (time.DueTimes[0], result.Error?.Message));
    }

    [Fact]
    public async Task ARenderOverTheAudioLimitIsTooLarge()
    {
        await using EngineStubServer stub = await EngineStubServer.StartAsync(speechBody: new byte[100]);
        using HttpClient http = SpeechClient.CreateHttpClient();
        SpeechResult result = await new SpeechClient(http, stub.BaseUri, TimeSpan.FromSeconds(10), limits: new SpeechLimits(99, 1000))
            .RenderAsync("hello there", "v1", 1.0, CancellationToken.None);
        Assert.Equal(SpeechErrorKind.TooLarge, result.Error?.Kind);
    }

    [Fact]
    public async Task ARenderExactlyAtTheAudioLimitIsKept()
    {
        await using EngineStubServer stub = await EngineStubServer.StartAsync(speechBody: new byte[100]);
        using HttpClient http = SpeechClient.CreateHttpClient();
        SpeechResult result = await new SpeechClient(http, stub.BaseUri, TimeSpan.FromSeconds(10), limits: new SpeechLimits(100, 1000))
            .RenderAsync("hello there", "v1", 1.0, CancellationToken.None);
        Assert.Equal(100, result.Audio.Length);
    }

    [Fact]
    public async Task AVoicesListingOverItsLimitIsTooLarge()
    {
        await using EngineStubServer stub = await EngineStubServer.StartAsync(voicesJson: """{"voices":["a","b"]}""");
        using HttpClient http = SpeechClient.CreateHttpClient();
        VoiceListResult result = await new SpeechClient(http, stub.BaseUri, TimeSpan.FromSeconds(10), limits: new SpeechLimits(1000, 10))
            .ListVoicesAsync(CancellationToken.None);
        Assert.Equal(SpeechErrorKind.TooLarge, result.Error?.Kind);
    }

    [Fact]
    public void TheDefaultLimitsAre64MiBOfSpeechAnd1MiBOfVoices() =>
        Assert.Equal((64L * 1024 * 1024, 1024L * 1024), (SpeechLimits.Default.MaxAudioBytes, SpeechLimits.Default.MaxVoicesBytes));

    [Fact]
    public async Task ARefusedConnectionIsUnreachable()
    {
        Uri gone;
        await using (EngineStubServer stub = await EngineStubServer.StartAsync())
        {
            gone = stub.BaseUri;
        }

        using HttpClient http = SpeechClient.CreateHttpClient();
        SpeechResult result = await new SpeechClient(http, gone, TimeSpan.FromSeconds(10)).RenderAsync("hello there", "v1", 1.0, CancellationToken.None);
        Assert.Equal(SpeechErrorKind.Unreachable, result.Error?.Kind);
    }

    [Fact]
    public async Task AConnectionDroppedMidBodyIsAFailedRender()
    {
        await using EngineStubServer stub = await EngineStubServer.StartAsync(dropMidBody: true);
        SpeechResult result = await RenderAgainst(stub, TimeSpan.FromSeconds(10));
        Assert.False(result.Ok);
    }

    [Fact]
    public async Task AConnectionDroppedMidVoicesBodyIsAnError()
    {
        await using EngineStubServer stub = await EngineStubServer.StartAsync(dropMidBody: true);
        using HttpClient http = SpeechClient.CreateHttpClient();
        VoiceListResult result = await new SpeechClient(http, stub.BaseUri, TimeSpan.FromSeconds(10)).ListVoicesAsync(CancellationToken.None);
        Assert.Equal(SpeechErrorKind.Unreachable, result.Error?.Kind);
    }

    [Fact]
    public async Task AFailedRenderIsNotOk()
    {
        await using EngineStubServer stub = await EngineStubServer.StartAsync(speechStatus: 503, speechBody: "no"u8.ToArray());
        Assert.False((await RenderAgainst(stub, TimeSpan.FromSeconds(10))).Ok);
    }

    [Fact]
    public async Task AMissingVoicesRouteIsAnHttpError()
    {
        await using EngineStubServer stub = await EngineStubServer.StartAsync(voicesJson: null);
        using HttpClient http = SpeechClient.CreateHttpClient();
        VoiceListResult result = await new SpeechClient(http, stub.BaseUri, TimeSpan.FromSeconds(10)).ListVoicesAsync(CancellationToken.None);
        Assert.Equal(SpeechErrorKind.HttpStatus, result.Error?.Kind);
    }

    [Fact]
    public async Task AnEmptyVoicesListIsAnError()
    {
        await using EngineStubServer stub = await EngineStubServer.StartAsync(voicesJson: """{"voices":[]}""");
        using HttpClient http = SpeechClient.CreateHttpClient();
        VoiceListResult result = await new SpeechClient(http, stub.BaseUri, TimeSpan.FromSeconds(10)).ListVoicesAsync(CancellationToken.None);
        Assert.Equal(SpeechErrorKind.EmptyBody, result.Error?.Kind);
    }

    [Fact]
    public async Task VoiceObjectsWithAnIdAreListed()
    {
        await using EngineStubServer stub = await EngineStubServer.StartAsync(voicesJson: """{"voices":[{"id":"x","name":"x"}]}""");
        using HttpClient http = SpeechClient.CreateHttpClient();
        VoiceListResult result = await new SpeechClient(http, stub.BaseUri, TimeSpan.FromSeconds(10)).ListVoicesAsync(CancellationToken.None);
        Assert.Equal(["x"], result.VoiceIds);
    }

    [Fact]
    public async Task TheCallersCancellationStillPropagates()
    {
        await using EngineStubServer stub = await EngineStubServer.StartAsync(speechDelay: TimeSpan.FromSeconds(5));
        using HttpClient http = SpeechClient.CreateHttpClient();
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SpeechClient(http, stub.BaseUri, TimeSpan.FromSeconds(30)).RenderAsync("hello there", "v1", 1.0, cts.Token));
    }
}
