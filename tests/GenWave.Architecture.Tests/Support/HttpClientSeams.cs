namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// L3's named, dated constant (SPEC F105.1, STORY-291 AC1): every production type allowed to
/// construct or acquire a working outbound HTTP client — by typed-client constructor injection, a raw
/// <c>new HttpClient(...)</c>, resolving one from <c>IHttpClientFactory.CreateClient</c>, or building
/// the lower-level handler/invoker family directly — is named here, once, so a stray outbound-HTTP
/// surface anywhere else in the graph is enumerable and red (ARCHITECTURE.md "Architecture
/// governance", L3's why: SSRF surface control). The detector itself is
/// <see cref="HttpClientMetadataScan"/>, not ArchUnitNET — see its remarks for why.
///
/// <b>The forbidden family (widened from just <c>HttpClient</c>).</b> <c>new
/// HttpMessageInvoker(new SocketsHttpHandler())</c> is a fully working outbound client that never
/// touches the <see cref="System.Net.Http.HttpClient"/> type at all — a forbid scoped to only
/// <c>HttpClient</c> green-lights it. <see cref="ForbiddenTypes"/> widens the forbid to the whole
/// construction/invocation family: <c>HttpClient</c>, <c>HttpMessageInvoker</c>,
/// <c>HttpMessageHandler</c>, <c>HttpClientHandler</c>, <c>SocketsHttpHandler</c>. Verified against
/// the real production graph: the widened forbid still finds exactly the types
/// <see cref="DesignatedSeams"/> below names — including <c>Program</c>'s composition-root wiring —
/// zero false positives — nothing in GenWave's production code touches
/// <c>HttpMessageInvoker</c>/<c>HttpMessageHandler</c>/<c>HttpClientHandler</c>/
/// <c>SocketsHttpHandler</c> outside those same seam sites (Program.cs's
/// <c>ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler {...})</c> is exactly the kind of
/// construction the wider forbid exists to catch). Deliberately count-free here (a hardcoded count in
/// this prose has drifted out of sync with <see cref="DesignatedSeams"/> before) — the list itself,
/// and the tests that assert against it, are the source of truth for how many seams exist.
///
/// <b>Program.cs is now in scope.</b> <see cref="HttpClientMetadataScan"/> reads every
/// <c>TypeDefinition</c> row directly, including the compiler-generated <c>Program</c> class
/// top-level statements compile to (method name <c>&lt;Main&gt;$</c>) — unlike the ArchUnitNET-based
/// detector this replaced, which treated that compiler-generated method as unscannable. Its three
/// <c>AddHttpClient</c> registrations are consequently a real, seam-listed member below, same as any
/// other composition-root code; <c>Specs.FeatureConventionLaws.ScenarioL3ProgramCompositionRoot</c>
/// (Story291_ConventionLaws.cs) additionally pins Program.cs's exact registration count and its
/// <c>AllowAutoRedirect = false</c> line by source text, since a construction scan can see that a
/// handler was built but not the boolean flag it was built with.
/// </summary>
internal static class HttpClientSeams
{
    /// <summary>The construction/acquisition family this law forbids outside the designated seam
    /// list (namespace, name) — a raw <c>new HttpClient()</c>, an injected/constructed client, or any
    /// of the lower-level pieces from which a working outbound client can be assembled without ever
    /// naming <c>HttpClient</c> itself.</summary>
    public static readonly IReadOnlyList<(string Namespace, string Name)> ForbiddenTypes = new[]
    {
        ("System.Net.Http", "HttpClient"),
        ("System.Net.Http", "HttpMessageInvoker"),
        ("System.Net.Http", "HttpMessageHandler"),
        ("System.Net.Http", "HttpClientHandler"),
        ("System.Net.Http", "SocketsHttpHandler"),
    };

    /// <summary>The designated seam list (STORY-291 AC1's "named constant in the suite"), discovered
    /// by searching <c>src/</c> for every actual HttpClient construction/injection/factory-ask site
    /// at T212 adoption — not assumed from the law's prose. Full type names, not <c>typeof</c>
    /// anchors: two entries (<c>CatalogHttpFetcher</c>, <c>LlmWishParser</c>) are <c>internal</c> to
    /// <c>GenWave.Host</c> with no <c>InternalsVisibleTo</c> grant into this test project, and
    /// <c>Program</c> is a compiler-generated top-level-statement type with no source-level name to
    /// anchor a <c>typeof</c> to at all — so a uniform string list, matching how every other law
    /// already names its members (<see cref="LawViolation.Member"/>,
    /// <see cref="ArchitectureExemption.Member"/>), beats a list that's <c>typeof</c> for most
    /// entries and a string for the rest.
    ///
    /// One entry per production type this suite's detector finds depending on the
    /// <see cref="ForbiddenTypes"/> family: TTS/LLM (typed-client injection; Kokoro/Piper/Ollama
    /// synthesis, voice listing, health probes, LLM copywriting) plus its composition root
    /// (<c>TtsServiceCollectionExtensions</c>, whose <c>AddHttpClient&lt;T&gt;</c> calls are the
    /// construction site the DI container itself never exposes); MediaLibrary's Ollama
    /// mood/explicit enrichment and MusicBrainz year lookup (same shape) plus its composition root
    /// (<c>MediaLibraryServiceCollectionExtensions</c>); Host's stats pollers (typed clients) and the
    /// two <c>IHttpClientFactory.CreateClient</c> askers — <c>CatalogHttpFetcher</c> (the helper
    /// <c>CatalogProxyService</c> — ARCHITECTURE's named seam — delegates its actual fetch to; the
    /// service itself never touches <c>HttpClient</c> directly, only <c>IHttpClientFactory</c>, so
    /// never appears here) and <c>LlmWishParser</c> (reuses <see cref="GenWave.Tts.LlmCopyWriter"/>'s
    /// named client rather than minting a second one); and <c>Program</c> itself, Host's composition
    /// root, whose three <c>AddHttpClient</c> registrations (two typed clients, one named client with
    /// a hand-built <c>HttpClientHandler</c>) are the construction sites behind all of the
    /// above.</summary>
    public static readonly IReadOnlyList<string> DesignatedSeams = new[]
    {
        // TTS/LLM (GenWave.Tts) — typed-client injection.
        "GenWave.Tts.KokoroTtsSynthesizer",
        "GenWave.Tts.KokoroVoiceLister",
        "GenWave.Tts.KokoroFallbackRenderer",
        "GenWave.Tts.KokoroHealthProbe",
        "GenWave.Tts.PiperTtsSynthesizer",
        // Piper as PRIMARY (SPEC F99.4, STORY-257, PLAN T148) — a second typed-client seam
        // alongside PiperTtsSynthesizer above, and the shared wire-mechanics helper the outbound
        // http.PostAsync call itself now lives in (PiperWireProtocol.RenderAsync takes the
        // already-constructed HttpClient as a plain parameter rather than a typed-client
        // constructor — the real egress point moved here when PiperTtsSynthesizer/
        // PiperPrimaryTtsSynthesizer were split from one copy of this logic into two callers).
        "GenWave.Tts.PiperPrimaryTtsSynthesizer",
        "GenWave.Tts.PiperWireProtocol",
        "GenWave.Tts.PiperHealthProbe",
        "GenWave.Tts.OllamaHealthProbe",
        "GenWave.Tts.LlmCopyWriter",
        "GenWave.Tts.TtsServiceCollectionExtensions",

        // MediaLibrary enrichment (Ollama mood/explicit, MusicBrainz year lookup).
        "GenWave.MediaLibrary.Mood.OllamaMoodTagger",
        "GenWave.MediaLibrary.ExplicitClassification.OllamaExplicitClassifier",
        "GenWave.MediaLibrary.YearLookup.MusicBrainzYearLookup",
        "GenWave.MediaLibrary.MediaLibraryServiceCollectionExtensions",

        // Context providers (SPEC F108/F109, PLAN T227/T228) — the weather + history providers' typed
        // clients, plus their shared composition root (ContextServiceCollectionExtensions.AddHttpClient<T>'s
        // configure delegate references HttpClient directly, same shape as every other
        // *ServiceCollectionExtensions entry above/below).
        "GenWave.Context.Weather.WeatherContextProvider",
        "GenWave.Context.History.HistoryContextProvider",
        "GenWave.Context.ContextServiceCollectionExtensions",

        // Host: stats polling (typed clients) + IHttpClientFactory askers + the composition root.
        "GenWave.Host.Stats.IcecastListenerStatsSource",
        "GenWave.Host.Stats.DockerContainerStatsSource",
        "GenWave.Host.Catalog.CatalogHttpFetcher",
        "GenWave.Host.Requests.LlmWishParser",
        "Program",
    };

    /// <summary>Evaluates "no type in <paramref name="assemblyPaths"/> outside
    /// <paramref name="isDesignatedSeam"/> depends on <see cref="ForbiddenTypes"/>", returning one
    /// <see cref="LawViolation"/> per offending type. The same function backs both the production
    /// fact (subjects = the eight GenWave assemblies, seam filter = <see cref="DesignatedSeams"/>) and
    /// the fixture self-proof (subjects = a fixture assembly, seam filter = the fixture's own
    /// stand-in list) — one detector, exercised both ways, not a copy kept in sync by hand.</summary>
    public static IReadOnlyList<LawViolation> FindViolations(
        IEnumerable<string> assemblyPaths, Func<string, bool> isDesignatedSeam)
    {
        var violations = new List<LawViolation>();

        foreach (var assemblyPath in assemblyPaths)
        {
            foreach (var (typeFullName, forbiddenName) in HttpClientMetadataScan.FindReferencingTypes(assemblyPath, ForbiddenTypes))
            {
                if (isDesignatedSeam(typeFullName))
                    continue;

                violations.Add(new LawViolation(LawId.L3, typeFullName, $"constructs or depends on \"{forbiddenName}\""));
            }
        }

        return violations;
    }
}
