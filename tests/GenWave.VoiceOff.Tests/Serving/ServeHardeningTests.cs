using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Http;
using GenWave.VoiceOff.Serving;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Serving;

/// <summary>The host must not take its environment from the machine it runs on, and every answer carries the same headers and the same body limit.</summary>
public static class ServeHardeningTests
{
    private static int FreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    [Collection("ProcessEnvironment")]
    public sealed class ScenarioTheMachineSaysDevelopment : IDisposable
    {
        private readonly ScopedEnvironmentVariable environment = new("ASPNETCORE_ENVIRONMENT", "Development");
        private readonly ServeScenario serve = new();
        private readonly HttpResponseMessage answer;
        private readonly string body;

        public ScenarioTheMachineSaysDevelopment()
        {
            // A folder where judgement.json belongs makes the save throw: a real 500 from a real route.
            Directory.CreateDirectory(serve.JudgementPath);
            answer = serve.Put(ServeScenario.Judgement([ServeScenario.First]));
            body = answer.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        [Fact]
        public void TheEnvironmentIsProduction() => Assert.Equal("Production", serve.EnvironmentName);

        [Fact]
        public void TheFailureAnswers500() => Assert.Equal(HttpStatusCode.InternalServerError, answer.StatusCode);

        [Fact]
        public void TheFailureIsNotSniffable() => Assert.Contains("nosniff", answer.Headers.GetValues("X-Content-Type-Options"));

        [Fact]
        public void TheFailureIsNotCached() => Assert.Equal("no-store", answer.Headers.CacheControl?.ToString());

        [Fact]
        public void TheAnswerShowsNoStackTrace() => Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);

        [Fact]
        public void TheAnswerNamesNoSourceFile() => Assert.DoesNotContain("GenWave", body, StringComparison.Ordinal);

        [Fact]
        public void TheErrorWriterHoldsALineNamingTheRequestAndTheExceptionType() =>
            Assert.Matches(@"^PUT /judgement failed: \w+Exception: ", serve.Stderr);

        [Fact]
        public void TheErrorWriterHoldsNoMoreThanThatLine() => Assert.Single(serve.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        public void Dispose()
        {
            serve.Dispose();
            environment.Dispose();
        }
    }

    public sealed class ScenarioJudgementJsonGoesBadWhileServing : IDisposable
    {
        private readonly ServeScenario serve = new();
        private readonly HttpResponseMessage answer;
        private readonly string body;

        public ScenarioJudgementJsonGoesBadWhileServing()
        {
            File.WriteAllText(serve.JudgementPath, "not json");
            answer = serve.Get("state");
            body = answer.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        [Fact]
        public void StateAnswers500() => Assert.Equal(HttpStatusCode.InternalServerError, answer.StatusCode);

        [Fact]
        public void TheBodyNamesNoInternals() => Assert.DoesNotContain("GenWave", body, StringComparison.Ordinal);

        [Fact]
        public void TheBodyIsTheGenericText() => Assert.Equal("The server could not complete the request.", body);

        [Fact]
        public void TheAnswerCarriesTheHeaders() => Assert.Equal("no-store", answer.Headers.GetValues("Cache-Control").Single());

        [Fact]
        public void TheErrorWriterNamesTheFile() => Assert.Contains("judgement.json", serve.Stderr);

        public void Dispose() => serve.Dispose();
    }

    [Collection("ProcessEnvironment")]
    public sealed class ScenarioAConfigFileNamesAnotherListener : IDisposable
    {
        private readonly ServeScenario serve = new(
            bind: IPAddress.Loopback,
            beforeStart: runDirectory => File.WriteAllText(
                Path.Combine(runDirectory, "appsettings.json"),
                $$"""{"urls":"http://0.0.0.0:{{FreePort()}}","preferHostingUrls":"true"}"""));

        [Fact]
        public void OneEndpointIsBound() => Assert.Single(serve.BoundUrls);

        [Fact]
        public void ItIsTheLoopbackOneThatWasAsked() => Assert.Equal("127.0.0.1", serve.BoundUrls[0].Host);

        public void Dispose() => serve.Dispose();
    }

    [Collection("ProcessEnvironment")]
    public sealed class ScenarioBEnvironmentNamesAnotherListener : IDisposable
    {
        private readonly ScopedEnvironmentVariable urls = new("ASPNETCORE_URLS", $"http://0.0.0.0:{FreePort()}");
        private readonly ScopedEnvironmentVariable prefer = new("ASPNETCORE_PREFERHOSTINGURLS", "true");
        private readonly ServeScenario serve = new(bind: IPAddress.Loopback);

        [Fact]
        public void OneEndpointIsBound() => Assert.Single(serve.BoundUrls);

        [Fact]
        public void ItIsTheLoopbackOneThatWasAsked() => Assert.Equal("127.0.0.1", serve.BoundUrls[0].Host);

        public void Dispose()
        {
            serve.Dispose();
            prefer.Dispose();
            urls.Dispose();
        }
    }

    public sealed class ScenarioTheSecurityHeaders : IDisposable
    {
        private readonly ServeScenario serve = new();
        private readonly HttpResponseMessage page;
        private readonly HttpResponseMessage refused;

        public ScenarioTheSecurityHeaders()
        {
            page = serve.Get("");
            refused = serve.Put("nonsense");
        }

        [Fact]
        public void CarriesTheContentSecurityPolicy() =>
            Assert.Equal(SecurityHeaders.ContentSecurityPolicy, page.Headers.GetValues("Content-Security-Policy").Single());

        [Fact]
        public void TheContentSecurityPolicyIsTheAgreedText() => Assert.Equal(
            "default-src 'self'; media-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'",
            SecurityHeaders.ContentSecurityPolicy);

        [Fact]
        public void CarriesNoSniff() => Assert.Equal("nosniff", page.Headers.GetValues("X-Content-Type-Options").Single());

        [Fact]
        public void CarriesNoReferrer() => Assert.Equal("no-referrer", page.Headers.GetValues("Referrer-Policy").Single());

        [Fact]
        public void CarriesNoStore() => Assert.Equal("no-store", page.Headers.GetValues("Cache-Control").Single());

        [Fact]
        public void NamesNoServer() => Assert.False(page.Headers.Contains("Server"));

        [Theory]
        [InlineData("state")]
        [InlineData("clips/aaa-first.wav")]
        [InlineData("nothing-here")]
        public void EveryAnswerCarriesTheHeaders(string path) => Assert.Equal("no-store", serve.Get(path).Headers.GetValues("Cache-Control").Single());

        [Fact]
        public void ARefusedSaveCarriesTheHeaders() => Assert.Equal("nosniff", refused.Headers.GetValues("X-Content-Type-Options").Single());

        public void Dispose() => serve.Dispose();
    }

    public sealed class ScenarioTheFailureLine
    {
        private readonly string line;

        public ScenarioTheFailureLine()
        {
            DefaultHttpContext http = new();
            http.Request.Method = "PUT";
            http.Request.Path = "/judgement";
            line = ServeHost.FailureLine(http.Request, new InvalidOperationException("a\r\nb"));
        }

        [Fact]
        public void ItHasNoCarriageReturn() => Assert.DoesNotContain('\r', line);

        [Fact]
        public void ItHasNoLineFeed() => Assert.DoesNotContain('\n', line);
    }

    public sealed class ScenarioTheBodyLimit : IDisposable
    {
        private const int Limit = 64 * 1024;
        private readonly ServeScenario serve = new();

        // The same valid judgement padded with trailing spaces to a byte count.
        private static string Padded(int bytes)
        {
            string body = ServeScenario.Judgement([ServeScenario.First]);
            return body + new string(' ', bytes - Encoding.UTF8.GetByteCount(body));
        }

        [Fact]
        public void ABodyOfExactly64KBIsRead() => Assert.Equal(HttpStatusCode.OK, serve.Put(Padded(Limit)).StatusCode);

        [Fact]
        public void ABodyOf64KBPlusOneAnswers413() => Assert.Equal(HttpStatusCode.RequestEntityTooLarge, serve.Put(Padded(Limit + 1)).StatusCode);

        [Fact]
        public void AChunkedBodyWithoutContentLengthOver64KBAnswers413()
        {
            using UnknownLengthContent content = new(Encoding.UTF8.GetBytes(Padded(Limit + 1024)));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, serve.Send(content).StatusCode);
        }

        [Fact]
        public void ATextPlainBodyAnswers415() =>
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, serve.Put(ServeScenario.Judgement([ServeScenario.First]), "text/plain").StatusCode);

        [Fact]
        public void ACharsetOnJsonIsFine() =>
            Assert.Equal(HttpStatusCode.OK, serve.Put(ServeScenario.Judgement([ServeScenario.First]), "application/json; charset=utf-8").StatusCode);

        public void Dispose() => serve.Dispose();
    }
}
