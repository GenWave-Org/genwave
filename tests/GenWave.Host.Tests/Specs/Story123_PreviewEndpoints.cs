// STORY-123 — Preview a persona before putting it on air (WIRE)
//
// BDD specification — xUnit. Drives the deployed entry points: POST /api/personas/preview
// runs the REAL LlmCopyWriter with NO template fallback (a preview that silently falls
// back lies about the persona — the honest 502 is the feature); POST /api/tts/preview
// returns synchronous audio/wav, nothing persisted.
//
// In-process tests construct PersonaController/TtsPreviewController directly with fakes
// (mirrors Story120's/Story079's controller-direct idiom) — faking IPersonaPreviewWriter rather
// than standing up a real LLM: MockCompletionsServer lives in GenWave.Tts.Tests, which this
// project does not reference, so the preview seam itself is the fake boundary here. That seam's
// REAL implementation (LlmCopyWriter.WritePreviewAsync, reusing WriteAsync's own prompt-building
// and hygiene code against a real mock completions server) is covered directly in
// GenWave.Tts.Tests (Story123_PersonaPreviewWriter.cs).
//
// The two posture negatives (401 without a cookie, 415 without JSON) drive the real HTTP pipeline
// via WebApplicationFactory<Program> (mirrors Story120's PersonaApiWebFactory) since they are
// properties of the auth/routing middleware, not of the controller's own logic.
//
// See docs/PLAN.md Epic T.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Options;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>Minimal <see cref="IOptionsMonitor{T}"/> that returns <see cref="CurrentValue"/> on every read.</summary>
file sealed class FakeOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>
/// Scriptable <see cref="IPersonaPreviewWriter"/> double: the fake boundary for these in-process
/// specs (see the file header for why the real <c>LlmCopyWriter.WritePreviewAsync</c> is exercised
/// elsewhere). Records the exact <see cref="SegmentRequest"/> + persona override
/// <see cref="PersonaController.Preview"/> built, so a scenario can assert what reached the writer.
/// </summary>
file sealed class FakePersonaPreviewWriter : IPersonaPreviewWriter
{
    public PersonaPreviewResult Result { get; set; } = new PersonaPreviewResult.Success("stub copy");
    public SegmentRequest? LastRequest { get; private set; }
    public Persona? LastPersonaOverride { get; private set; }

    public Task<PersonaPreviewResult> WritePreviewAsync(
        SegmentRequest request, Persona? personaOverride, CancellationToken ct)
    {
        LastRequest = request;
        LastPersonaOverride = personaOverride;
        return Task.FromResult(Result);
    }
}

/// <summary>In-memory <see cref="IPersonaStore"/> double — only <c>GetByIdAsync</c> is reachable
/// through <see cref="PersonaController.Preview"/>; every write throws if ever hit by mistake.</summary>
file sealed class FakePersonaStore : IPersonaStore
{
    public Dictionary<long, Persona> Rows { get; } = [];

    public Task<IReadOnlyList<Persona>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Persona>>(Rows.Values.ToList());

    public Task<Persona?> GetByIdAsync(long id, CancellationToken ct) =>
        Task.FromResult(Rows.TryGetValue(id, out var persona) ? persona : null);

    public Task<PersonaWriteResult> CreateAsync(PersonaDraft draft, CancellationToken ct) =>
        throw new NotSupportedException("Preview never writes through IPersonaStore.");

    public Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct) =>
        throw new NotSupportedException("Preview never writes through IPersonaStore.");

    public Task<PersonaWriteResult> DeleteAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Preview never writes through IPersonaStore.");

    // Never reached by these scenarios either (FakeActivePersonaAccessor below only overrides
    // ResolveAsync — ResolveCardAsync stays the interface's own no-card default) — a plain null is
    // enough to satisfy IPersonaStore without asserting anything about a path nothing here exercises.
    public Task<PersonaCard?> GetCardByIdAsync(long id, CancellationToken ct) =>
        Task.FromResult<PersonaCard?>(null);
}

/// <summary>Unused-by-preview <see cref="IStationSettingsStore"/> double — the constructor
/// dependency exists for the sibling CRUD actions, none of which these scenarios call.</summary>
file sealed class NotUsedStationSettingsStore : IStationSettingsStore
{
    public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by Story123's preview scenarios.");

    public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
}

/// <summary>Scriptable <see cref="IActivePersonaAccessor"/> double for the "neither personaId nor
/// draft" default-to-active-persona case (F35.6).</summary>
file sealed class FakeActivePersonaAccessor : IActivePersonaAccessor
{
    public Persona? Persona { get; set; }

    public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult(Persona);
}

/// <summary>Scriptable <see cref="IAdminMediaLookup"/> double — register rows by id via <see cref="Add"/>.</summary>
file sealed class FakeAdminMediaLookup : IAdminMediaLookup
{
    readonly Dictionary<long, (AdminMediaDto Row, long LibraryId)> rows = [];

    public void Add(long id, AdminMediaDto row, long libraryId) => rows[id] = (row, libraryId);

    public Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(long id, CancellationToken ct) =>
        Task.FromResult(rows.TryGetValue(id, out var found) ? found : ((AdminMediaDto Row, long LibraryId)?)null);
}

/// <summary>
/// Records the request it was called with and writes a placeholder file to
/// <paramref name="outputDirectory"/>, mirroring the real <see cref="ITtsSynthesizer"/> contract
/// (a synth call is a disk write) closely enough to prove
/// <see cref="TtsPreviewController"/>'s cleanup (Story096's <c>RecordingTtsSynthesizer</c> pattern).
/// </summary>
file sealed class RecordingTtsSynthesizer(string outputDirectory) : ITtsSynthesizer
{
    public byte[] Bytes { get; set; } = [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0]; // "RIFF...."
    public string? LastText { get; private set; }
    public string? LastVoice { get; private set; }
    public string? LastPath { get; private set; }
    public bool ThrowOnSynthesize { get; set; }
    public bool DelayPastBudget { get; set; }

    public async Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct)
    {
        LastText = text;
        LastVoice = voice;

        if (ThrowOnSynthesize)
            throw new InvalidOperationException("Synthesis failed (test double).");

        if (DelayPastBudget)
            await Task.Delay(TimeSpan.FromSeconds(30), ct);

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, $"{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(path, Bytes, ct);
        LastPath = path;
        return path;
    }
}

/// <summary>Builds a <see cref="PersonaController"/>/<see cref="TtsPreviewController"/> wired to the given fakes.</summary>
file static class PreviewControllerFactory
{
    public static PersonaController BuildPersonaController(
        IPersonaPreviewWriter previewWriter,
        IActivePersonaAccessor personaAccessor,
        IAdminMediaLookup mediaLookup,
        IPersonaStore? personaStore = null,
        StationOptions? stationOptions = null,
        IStationScopeProvider? scopeProvider = null) =>
        new(
            personaStore ?? new FakePersonaStore(),
            new NotUsedStationSettingsStore(),
            new FakeOptionsMonitor<StationOptions>(stationOptions ?? DefaultStationOptions()),
            previewWriter,
            personaAccessor,
            mediaLookup,
            scopeProvider ?? new FakeStationScopeProvider(new LibraryScope([1])),
            NullLogger<PersonaController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    public static TtsPreviewController BuildTtsController(
        ITtsSynthesizer synthesizer, StationOptions? stationOptions = null, TtsOptions? ttsOptions = null) =>
        new(
            synthesizer,
            new FakeOptionsMonitor<StationOptions>(stationOptions ?? DefaultStationOptions()),
            new FakeOptionsMonitor<TtsOptions>(ttsOptions ?? new TtsOptions()),
            NullLogger<TtsPreviewController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    public static StationOptions DefaultStationOptions() => new()
    {
        Id    = "test",
        Name  = "Test Station",
        Voice = "af_heart",
    };

    public static AdminMediaDto SampleRow(long id) => new(
        MediaId:        id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Locator:        $"/media/{id}.mp3",
        Format:         "mp3",
        State:          "ready",
        DurationMs:     180_000,
        Title:          "Astral Plane",
        Artist:         "Valerie June",
        Album:          "The Order of Time",
        Genre:          "Folk",
        Year:           2017,
        IntegratedLufs: -16.0,
        TruePeakDbtp:   -1.5,
        Measurable:     true,
        CueInSec:       null,
        CueOutSec:      null,
        Eligible:       true,
        Version:        "1");
}

// ── WebApplicationFactory for auth/content-type AC tests ─────────────────────────────────────────

/// <summary>
/// Minimal <see cref="WebApplicationFactory{TEntryPoint}"/> that brings up the real HTTP pipeline
/// (routing, auth, content-type negotiation) while removing hosted services that would attempt
/// real Liquidsoap/Postgres connections. Mirrors Story120's <c>PersonaApiWebFactory</c>: neither
/// posture scenario ever resolves <see cref="IPersonaPreviewWriter"/> (401 is rejected by auth
/// middleware, 415 by action-selection) — both happen before <see cref="PersonaController"/> is
/// constructed — so the persona store's connection string is left at its (empty, dev-mode) default.
/// </summary>
file sealed class PersonaPreviewApiWebFactory(bool withAdminPassword) : WebApplicationFactory<Program>
{
    const string LibraryConnVar = "ConnectionStrings__Library";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint
        // so ValidateOnStart() is satisfied without injecting them manually.
        builder.UseEnvironment("Development");

        if (withAdminPassword)
        {
            builder.UseSetting("Admin:Password", "test-password-x7z");
        }

        builder.ConfigureTestServices(services =>
        {
            // Remove ALL hosted services — no Liquidsoap or DB connections during this test.
            services.RemoveAll<IHostedService>();
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var prev = Environment.GetEnvironmentVariable(LibraryConnVar);
        Environment.SetEnvironmentVariable(LibraryConnVar, "Host=nowhere;Database=test");
        try
        {
            return base.CreateHost(builder);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LibraryConnVar, prev);
        }
    }
}

// ── In-process tests ──────────────────────────────────────────────────────────────────────────────

public static class FeaturePreviewEndpoints
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — both previews round-trip
    // ---------------------------------------------------------------------

    public sealed class ScenarioCopyPreview
    {
        [Fact]
        public async Task SavedPersonaPreviewReturns200WithTheLlmText()
        {
            // { personaId, mediaId } against the mock LLM → 200 { text } (F35.6, AC1).
            var now = DateTime.UtcNow;
            var persona = new Persona(7, "Neon Nightowl", "Spins vinyl til dawn.", "moody, late-night", "af_sky", now, now);
            var store = new FakePersonaStore();
            store.Rows[7] = persona;
            var mediaLookup = new FakeAdminMediaLookup();
            mediaLookup.Add(42, PreviewControllerFactory.SampleRow(42), libraryId: 1);
            var writer = new FakePersonaPreviewWriter
            {
                Result = new PersonaPreviewResult.Success("Spinning up something great, stick around."),
            };
            var controller = PreviewControllerFactory.BuildPersonaController(
                writer, new FakeActivePersonaAccessor(), mediaLookup, personaStore: store);

            var result = await controller.Preview(
                new PersonaPreviewRequest(null, 42, 7, null, null, null, null), CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<PersonaPreviewResponse>(ok.Value);
            Assert.Equal("Spinning up something great, stick around.", response.Text);
            Assert.Equal(persona, writer.LastPersonaOverride);
            Assert.Equal("Astral Plane", writer.LastRequest?.Track?.Title);
        }

        [Fact]
        public async Task DraftFieldsPreviewReturns200WithTheLlmText()
        {
            // Unsaved { backstory, style, voice? } previews without persisting (F35.6, AC1).
            var writer = new FakePersonaPreviewWriter { Result = new PersonaPreviewResult.Success("Draft copy here.") };
            var controller = PreviewControllerFactory.BuildPersonaController(
                writer, new FakeActivePersonaAccessor(), new FakeAdminMediaLookup());

            var result = await controller.Preview(
                new PersonaPreviewRequest(null, null, null, "Test Draft", "A backstory.", "chill", "af_sky"),
                CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<PersonaPreviewResponse>(ok.Value);
            Assert.Equal("Draft copy here.", response.Text);
            // Unsaved sentinel id (never a real IPersonaStore row) — nothing was persisted.
            Assert.Equal(0, writer.LastPersonaOverride?.Id);
            Assert.Equal("A backstory.", writer.LastPersonaOverride?.Backstory);
            Assert.Equal("af_sky", writer.LastRequest?.Voice);
        }
    }

    public sealed class ScenarioAudioPreview : IDisposable
    {
        readonly string outputDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        [Fact]
        public async Task TtsPreviewReturnsAudioWavBytes()
        {
            // POST /api/tts/preview { text, voice? } → 200 audio/wav, synchronous (F35.6, AC2).
            var synth = new RecordingTtsSynthesizer(outputDirectory);
            var controller = PreviewControllerFactory.BuildTtsController(synth);

            var result = await controller.Preview(new TtsPreviewRequest("Hello there.", "af_heart"), CancellationToken.None);

            var file = Assert.IsType<FileContentResult>(result);
            Assert.Equal("audio/wav", file.ContentType);
            Assert.Equal(synth.Bytes, file.FileContents);
            Assert.Equal("af_heart", synth.LastVoice);
        }

        [Fact]
        public async Task PreviewArtifactsAreNotPersistedAndNeverEnterRotation()
        {
            // No catalog row, no cache file that a rotation path can select (F35.6, AC2).
            var synth = new RecordingTtsSynthesizer(outputDirectory);
            var controller = PreviewControllerFactory.BuildTtsController(synth);

            await controller.Preview(new TtsPreviewRequest("Hello there.", "af_heart"), CancellationToken.None);

            Assert.NotNull(synth.LastPath);
            Assert.False(File.Exists(synth.LastPath));
        }

        public void Dispose()
        {
            if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, recursive: true);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — honest failure, guarded surface
    // ---------------------------------------------------------------------

    public sealed class ScenarioLlmDownIsA502NotASilentFallback
    {
        [Fact]
        public async Task CopyPreviewReturns502ProblemDetailsWhenTheLlmFails()
        {
            // Down or stalling → 502; the template is NEVER substituted (F35.6, AC3).
            var writer = new FakePersonaPreviewWriter
            {
                Result = new PersonaPreviewResult.Failed("The LLM request failed. Check the server logs for details."),
            };
            var controller = PreviewControllerFactory.BuildPersonaController(
                writer, new FakeActivePersonaAccessor(), new FakeAdminMediaLookup());

            var result = await controller.Preview(
                new PersonaPreviewRequest(null, null, null, null, null, null, null), CancellationToken.None);

            var problem = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
            var details = Assert.IsType<ProblemDetails>(problem.Value);
            Assert.Equal(StatusCodes.Status502BadGateway, details.Status);
        }
    }

    public sealed class ScenarioTtsSynthesisFailureIsA502 : IDisposable
    {
        readonly string outputDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        [Fact]
        public async Task SynthesisThrowReturns502ProblemDetails()
        {
            // Synthesizer fault (connect refused, non-2xx, etc.) → 502 ProblemDetails, mirroring
            // SafeSegmentsController's render-failure shape (F35.6).
            var synth = new RecordingTtsSynthesizer(outputDirectory) { ThrowOnSynthesize = true };
            var controller = PreviewControllerFactory.BuildTtsController(synth);

            var result = await controller.Preview(
                new TtsPreviewRequest("Hello there.", "af_heart"), CancellationToken.None);

            var problem = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
            Assert.IsType<ProblemDetails>(problem.Value);
        }

        [Fact]
        public async Task ExceedingTheRenderBudgetReturns502ProblemDetails()
        {
            // A 1s Tts:RenderBudgetSeconds against a synthesizer that delays 30s proves the budget
            // is actually enforced (F35.6 — "within Tts:RenderBudgetSeconds") while keeping this
            // fact's own runtime to ~1s rather than waiting out the full delay.
            var synth = new RecordingTtsSynthesizer(outputDirectory) { DelayPastBudget = true };
            var controller = PreviewControllerFactory.BuildTtsController(
                synth, ttsOptions: new TtsOptions { RenderBudgetSeconds = 1 });

            var result = await controller.Preview(
                new TtsPreviewRequest("Hello there.", "af_heart"), CancellationToken.None);

            var problem = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
            Assert.IsType<ProblemDetails>(problem.Value);
        }

        public void Dispose()
        {
            if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, recursive: true);
        }
    }

    // PersonaPreviewApiWebFactory.CreateHost mutates the ConnectionStrings__Library process env var
    // for the boot window — shared with every other env-var-mutating factory in this test project
    // (Story056/058/084/112/120's factories), so this class opts into the serializing collection
    // (see EnvVarMutatingWebFactoryCollection) rather than racing them under xUnit's default parallelism.
    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioGates
    {
        [Fact]
        public async Task AnonymousPreviewReturns401WhenPasswordSet()
        {
            // (AC4).
            await using var factory = new PersonaPreviewApiWebFactory(withAdminPassword: true);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });

            var response = await client.PostAsJsonAsync("/api/personas/preview", new { });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task NonJsonBodyReturns415()
        {
            // [Consumes("application/json")] returns 415 Unsupported Media Type (AC4).
            await using var factory = new PersonaPreviewApiWebFactory(withAdminPassword: false);
            var client = factory.CreateClient();

            var body = new StringContent(
                "kind=LeadIn", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
            var response = await client.PostAsync("/api/personas/preview", body);

            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }
    }
}
