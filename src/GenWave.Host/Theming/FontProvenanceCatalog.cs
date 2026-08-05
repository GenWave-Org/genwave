namespace GenWave.Host.Theming;

using System.Reflection;
using System.Text.Json;

/// <summary>
/// Loads GenWave's font provenance record (FONTS.md; PLAN T188) — one <see cref="VendoredFontFace"/>
/// per face GenWave has vendored, keyed by its <c>/fonts/{file}</c> src. Embedded as an assembly
/// resource (<c>wwwroot/fonts/fonts-provenance.json</c>), mirroring <see cref="ThemeCatalog"/>'s own
/// shipped-manifest loading (<c>LoadShippedSources</c>) — no filesystem/hosting dependency, so this
/// loads identically in every environment, including tests that never set an
/// <c>IWebHostEnvironment.ContentRootPath</c>.
///
/// This IS the "GenWave-vendored curated set" SPEC F103.10 refers to — nothing else defines it, and
/// <see cref="ThemeFontProvenanceValidator"/> checks every theme manifest's font asset srcs against
/// exactly this record, never a second, independently-maintained list.
/// </summary>
public sealed class FontProvenanceCatalog
{
    const string ResourceName = "GenWave.Host.wwwroot.fonts.fonts-provenance.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    static readonly Lazy<FontProvenanceCatalog> lazyDefault = new(() => Parse(ReadEmbeddedJson()));

    /// <summary>The one instance GenWave's own runtime uses — loaded once, from this assembly's own
    /// embedded copy of <c>fonts-provenance.json</c>. A test that needs a DIFFERENT provenance set
    /// (e.g. PLAN T188's own byte-ceiling sad-path spec, which needs a face heavier than any real
    /// vendored one) builds its own via <see cref="Parse"/> instead of touching this singleton.
    /// </summary>
    public static FontProvenanceCatalog Default => lazyDefault.Value;

    /// <summary>Every vendored face, keyed by its <c>/fonts/{file}</c> src — the exact string shape
    /// <see cref="ThemeFontAsset.Src"/> carries.</summary>
    public IReadOnlyDictionary<string, VendoredFontFace> BySrc { get; }

    FontProvenanceCatalog(IReadOnlyDictionary<string, VendoredFontFace> bySrc) => BySrc = bySrc;

    /// <summary>
    /// Parses a provenance JSON document (the same <c>{"faces":[…]}</c> shape as the embedded
    /// resource) into a <see cref="BySrc"/> lookup. Public — not just the embedded-resource path —
    /// so a test can build a small fixture provenance record (PLAN T188's own "you may add a fake
    /// face entry in a TEST fixture provenance record, not the real one") without a second embedded
    /// resource of its own.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="json"/> is not well-formed JSON, declares no faces, a face is missing a
    /// required field, or two faces share the same <see cref="VendoredFontFace.Src"/> — this record
    /// is GenWave's own, first-party, build-time data, so a defect here is an authoring bug to fix,
    /// not a request-time condition, mirroring <see cref="ThemeManifestParser"/>'s own "fail loudly
    /// at load" posture for its own first-party shipped manifests.
    /// </exception>
    public static FontProvenanceCatalog Parse(string json)
    {
        ProvenanceDocumentJson? document;
        try
        {
            document = JsonSerializer.Deserialize<ProvenanceDocumentJson>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"font provenance record is malformed JSON ({ex.Message})", ex);
        }

        if (document?.Faces is not { Count: > 0 } faces)
            throw new InvalidOperationException("font provenance record declares no faces");

        var bySrc = new Dictionary<string, VendoredFontFace>(StringComparer.Ordinal);
        foreach (var raw in faces)
        {
            if (raw is not
                {
                    Family: { Length: > 0 } family,
                    File: { Length: > 0 } file,
                    SourceUrl: { Length: > 0 } sourceUrl,
                    License: { Length: > 0 } license,
                    Subset: { Length: > 0 } subset,
                    Bytes: > 0,
                })
                throw new InvalidOperationException("font provenance record has a face missing a required field");

            var face = new VendoredFontFace(family, file, sourceUrl, license, raw.Version, subset, raw.Bytes);
            if (!bySrc.TryAdd(face.Src, face))
                throw new InvalidOperationException($"font provenance record has a duplicate face src '{face.Src}'");
        }

        return new FontProvenanceCatalog(bySrc);
    }

    static string ReadEmbeddedJson()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"embedded font provenance resource '{ResourceName}' could not be opened");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Ephemeral JSON projection of the untrusted-shape-until-checked provenance document —
    /// mirrors <see cref="ThemeManifestParser"/>'s own all-nullable <c>*Json</c> idiom.</summary>
    sealed record ProvenanceDocumentJson
    {
        public IReadOnlyList<VendoredFontFaceJson>? Faces { get; init; }
    }

    /// <summary>Ephemeral JSON projection of one raw provenance entry.</summary>
    sealed record VendoredFontFaceJson
    {
        public string? Family { get; init; }
        public string? File { get; init; }
        public string? SourceUrl { get; init; }
        public string? License { get; init; }
        public string? Version { get; init; }
        public string? Subset { get; init; }
        public long Bytes { get; init; }
    }
}
