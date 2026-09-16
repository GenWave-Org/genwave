// STORY-399 — Jingle-pack install refuses whole (SPEC F165.5 · PLAN T414 review round 2 findings
// F1, F6.1, F6.8, F6.9, F8)
//
// F1 (CRITICAL): the all-or-nothing enrichment abort had no fact. A pack whose second asset fails to
// decode/measure must refuse the WHOLE install — nothing under Packs:JingleRoot, no store write —
// whether this is a fresh install or a reinstall of an already-installed slug (the previous install's
// own bytes/rows survive untouched, since staging/enrichment fails before ANY file is ever moved or
// any store call is made — see JinglePackController.Install's own gate order remarks).
//
// F6.1 (part of HIGH finding F6): a manifest-declared sha256 that does not match the asset's actual
// served bytes is withheld — CatalogInstallShell.FetchAllAssetsUncachedAsync's own hash-verify gate,
// shared with every other pack controller, not jingle-specific code. Its 502 WithheldProblem carries
// NO ProblemDetails.Type token (T414 review round 2, "Rulings that stand" — CatalogEntryFetchResult/
// CatalogAssetFetchResult.HashMismatch both map through the SAME shared shell as every other pack
// kind; widening that shared body is out of this task's own scope). F6.8's own ".staging-* never
// survives" is folded into every Scenario below rather than given its own file. F6.9 (cancellation)
// reuses this file's own EnrichmentFailureWebFactory harness in a two-instance dance mirroring
// Story395_VoicePackInstall.cs's own cancellation fact exactly.
//
// Uses FakeJinglePackStore + a local FakeAdsLibraryRepository (no real Postgres) — the enrichment/
// hash-mismatch gates both run BEFORE Install ever calls IJinglePackStore, so a fake dictionary proves
// "zero rows, no pack row" exactly as honestly as a real one for this specific failure shape (the
// STORE's own real-Postgres contract — orphan cleanup, station-first-with-compensation — is proven
// elsewhere: Story399_JinglePackInstall.cs, Story399_JinglePackReinstallOrphans.cs,
// Story401_PackUninstallGuards.cs).

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureJinglePackEnrichmentAndHashFailures
{
    // ---------------------------------------------------------------------
    // F1 — a fresh install's own corrupt asset aborts the whole pack
    // ---------------------------------------------------------------------

    public sealed class ScenarioFreshInstallWithACorruptAssetAborts
    {
        [Fact]
        public async Task TheInstallReturns422NamingTheUnreadableAssetType()
        {
            using var arc = await FreshCorruptArc.RunAsync();
            Assert.Equal(HttpStatusCode.UnprocessableEntity, arc.Status);
            Assert.Contains("\"jingle_asset_unreadable\"", arc.Body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task NothingLandsUnderTheJingleRootForThisSlug()
        {
            using var arc = await FreshCorruptArc.RunAsync();
            Assert.False(Directory.Exists(Path.Combine(arc.JingleRoot, FreshCorruptArc.Slug)));
        }

        [Fact]
        public async Task NoStagingSiblingSurvives()
        {
            using var arc = await FreshCorruptArc.RunAsync();
            // Staging dir is `{slug}.staging-{guid}` — a leading-dot glob ("`.staging-*`") can never
            // match; only the `*.staging-*` form does the work (T414 review round 3 finding 4).
            Assert.Empty(Directory.EnumerateDirectories(arc.JingleRoot, "*.staging-*"));
        }

        [Fact]
        public async Task TheStoreNeverReceivesAWrite()
        {
            using var arc = await FreshCorruptArc.RunAsync();
            Assert.Equal(0, arc.Store.PackCount);
        }
    }

    // ---------------------------------------------------------------------
    // T414 delta review finding 2 — a TagLib-unreadable-but-ffmpeg-measurable asset aborts the whole
    // pack on its own (the TagLib catch in StageAndEnrichAllAsync, distinct from the loudness gate
    // every plain-corrupt fixture trips first)
    // ---------------------------------------------------------------------

    public sealed class ScenarioATagLibUnreadableAssetAborts
    {
        [Fact]
        public async Task TheInstallReturns422NamingTheUnreadableAssetType()
        {
            using var arc = await TagLibUnreadableArc.RunAsync();
            Assert.Equal(HttpStatusCode.UnprocessableEntity, arc.Status);
            Assert.Contains("\"jingle_asset_unreadable\"", arc.Body, StringComparison.Ordinal);
            Assert.Contains(TagLibUnreadableArc.BadFile, arc.Body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task NothingLandsUnderTheJingleRootForThisSlug()
        {
            using var arc = await TagLibUnreadableArc.RunAsync();
            Assert.False(Directory.Exists(Path.Combine(arc.JingleRoot, TagLibUnreadableArc.Slug)));
        }

        [Fact]
        public async Task NoStagingSiblingSurvives()
        {
            using var arc = await TagLibUnreadableArc.RunAsync();
            Assert.Empty(Directory.EnumerateDirectories(arc.JingleRoot, "*.staging-*"));
        }

        [Fact]
        public async Task TheStoreNeverReceivesAWrite()
        {
            using var arc = await TagLibUnreadableArc.RunAsync();
            Assert.Equal(0, arc.Store.PackCount);
        }
    }

    // ---------------------------------------------------------------------
    // T414 delta review finding 2 — a zero-duration-per-TagLib-but-ffmpeg-measurable asset aborts the
    // whole pack on its own (the `Duration.TotalSeconds: > 0` gate in StageAndEnrichAllAsync, the OTHER
    // post-hash enrichment gate a corrupt/malformed asset can trip)
    // ---------------------------------------------------------------------

    public sealed class ScenarioAZeroDurationAssetAborts
    {
        [Fact]
        public async Task TheInstallReturns422NamingTheUnreadableAssetType()
        {
            using var arc = await ZeroDurationArc.RunAsync();
            Assert.Equal(HttpStatusCode.UnprocessableEntity, arc.Status);
            Assert.Contains("\"jingle_asset_unreadable\"", arc.Body, StringComparison.Ordinal);
            Assert.Contains(ZeroDurationArc.BadFile, arc.Body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task NothingLandsUnderTheJingleRootForThisSlug()
        {
            using var arc = await ZeroDurationArc.RunAsync();
            Assert.False(Directory.Exists(Path.Combine(arc.JingleRoot, ZeroDurationArc.Slug)));
        }

        [Fact]
        public async Task NoStagingSiblingSurvives()
        {
            using var arc = await ZeroDurationArc.RunAsync();
            Assert.Empty(Directory.EnumerateDirectories(arc.JingleRoot, "*.staging-*"));
        }

        [Fact]
        public async Task TheStoreNeverReceivesAWrite()
        {
            using var arc = await ZeroDurationArc.RunAsync();
            Assert.Equal(0, arc.Store.PackCount);
        }
    }

    // ---------------------------------------------------------------------
    // F1 — a reinstall's own corrupt asset leaves the PREVIOUS install untouched
    // ---------------------------------------------------------------------

    public sealed class ScenarioReinstallWithACorruptAssetPreservesThePreviousInstall
    {
        [Fact]
        public async Task TheSecondAttemptReturns422()
        {
            using var arc = await ReinstallCorruptArc.RunAsync();
            Assert.Equal(HttpStatusCode.UnprocessableEntity, arc.SecondStatus);
        }

        [Fact]
        public async Task EveryFirstInstallFileKeepsItsOriginalBytes()
        {
            using var arc = await ReinstallCorruptArc.RunAsync();
            Assert.Equal(arc.FirstKeptBytesHash, Sha256File(Path.Combine(arc.JingleRoot, ReinstallCorruptArc.Slug, ReinstallCorruptArc.KeptFile)));
            Assert.Equal(arc.FirstOtherBytesHash, Sha256File(Path.Combine(arc.JingleRoot, ReinstallCorruptArc.Slug, ReinstallCorruptArc.OtherFile)));
        }

        [Fact]
        public async Task TheStoreStillHoldsExactlyTheFirstInstall()
        {
            using var arc = await ReinstallCorruptArc.RunAsync();
            Assert.Equal(1, arc.Store.PackCount);
        }

        [Fact]
        public async Task NoPrevSiblingSurvives()
        {
            using var arc = await ReinstallCorruptArc.RunAsync();
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(arc.JingleRoot, ReinstallCorruptArc.Slug), "*.prev-*"));
        }

        [Fact]
        public async Task NoStagingSiblingSurvives()
        {
            using var arc = await ReinstallCorruptArc.RunAsync();
            Assert.Empty(Directory.EnumerateDirectories(arc.JingleRoot, "*.staging-*"));
        }

        static string Sha256File(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    }

    // ---------------------------------------------------------------------
    // F6.1 — a manifest-declared sha256 that does not match the served bytes is withheld
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnAssetHashMismatchIsWithheld
    {
        [Fact]
        public async Task TheInstallReturns502()
        {
            using var arc = await HashMismatchArc.RunAsync();
            Assert.Equal(HttpStatusCode.BadGateway, arc.Status);
        }

        [Fact]
        public async Task NothingLandsUnderTheJingleRootForThisSlug()
        {
            using var arc = await HashMismatchArc.RunAsync();
            Assert.False(Directory.Exists(Path.Combine(arc.JingleRoot, HashMismatchArc.Slug)));
        }

        [Fact]
        public async Task TheStoreNeverReceivesAWrite()
        {
            using var arc = await HashMismatchArc.RunAsync();
            Assert.Equal(0, arc.Store.PackCount);
        }
    }

    // ---------------------------------------------------------------------
    // Advisory 3 (T414 review round 2/3) — a manifest asset `file` cannot escape the jingle root
    // ---------------------------------------------------------------------

    /// <summary>
    /// Pins <see cref="CatalogJinglePackManifestSerializer"/>'s own anchored <c>FileFormat</c> pattern,
    /// <c>JinglePackController.CrossCheckManifestAssets</c> (<c>JinglePackController.cs:348-356</c>),
    /// and the move's own canonical-root re-check (<c>IsUnderCanonicalRoot</c>,
    /// <c>JinglePackController.cs:487-489</c>) as ONE closed defense-in-depth set. A manifest — an
    /// untrusted document straight off the catalog origin — that declares an asset <c>file</c> of
    /// <c>"../escape.wav"</c> is refused by the FIRST of the three
    /// (<see cref="CatalogJinglePackManifestSerializer.Deserialize"/>'s own <c>FileFormat</c>, which
    /// admits no <c>/</c> at all) before the manifest is even accepted, so
    /// <c>CrossCheckManifestAssets</c> and the move's own canonical-root re-check never run for this
    /// exact attack shape — that is by design, not a gap: T414 review round 3's own mutation (h)
    /// (removing <c>IsUnderCanonicalRoot</c> on the move) leaves this fact green precisely because the
    /// upstream gate already closed the door — see this Scenario's own git-blame commit message for
    /// that mutation's result. The catalog INDEX's own asset entry stays a perfectly ordinary, valid
    /// file (<c>CatalogIndexValidator</c>'s own separate asset-path pattern is a different layer,
    /// already its own test surface, not this controller's) — this fact's whole point is a MANIFEST
    /// that lies about its own asset's shape.
    /// </summary>
    public sealed class ScenarioAManifestAssetPathEscapeIsRefused
    {
        [Fact]
        public async Task TheInstallRefusesTheWholeManifest()
        {
            using var arc = await PathEscapeArc.RunAsync();
            // MalformedManifestProblem (CatalogInstallShell.cs:122-127) sets Status/Title/Detail but
            // no Type (a review-round-3 finding asked this to be verified empirically rather than
            // assumed). Title alone ("Malformed jingle pack manifest.") does NOT discriminate this
            // Problem factory from CatalogInstallShell.UndeclaredManifestAssetProblem's own — the two
            // share the exact same Title format — so assert on Detail ("...could not be parsed."),
            // which is what actually pins the SERIALIZER layer as the one that refused this manifest,
            // rather than CrossCheckManifestAssets catching a "../escape.wav" that slipped past a
            // loosened FileFormat (that failure mode's own Detail would instead read "...references
            // \"../escape.wav\"...").
            Assert.Equal(HttpStatusCode.BadRequest, arc.Status);
            Assert.Contains("\"Malformed jingle pack manifest.\"", arc.Body, StringComparison.Ordinal);
            var problem = JsonSerializer.Deserialize<ProblemDetails>(arc.Body);
            Assert.NotNull(problem);
            Assert.NotNull(problem.Detail);
            Assert.Contains("could not be parsed", problem.Detail, StringComparison.Ordinal);
        }

        [Fact]
        public async Task NothingLandsUnderTheJingleRootForThisSlug()
        {
            using var arc = await PathEscapeArc.RunAsync();
            Assert.False(Directory.Exists(Path.Combine(arc.JingleRoot, PathEscapeArc.Slug)));
        }

        [Fact]
        public async Task NoStagingSiblingSurvives()
        {
            using var arc = await PathEscapeArc.RunAsync();
            Assert.Empty(Directory.EnumerateDirectories(arc.JingleRoot, "*.staging-*"));
        }

        [Fact]
        public async Task TheStoreNeverReceivesAWrite()
        {
            using var arc = await PathEscapeArc.RunAsync();
            Assert.Equal(0, arc.Store.PackCount);
        }
    }

    // ---------------------------------------------------------------------
    // F6.9 — a cancelled upsert during a reinstall leaves the previous install untouched
    // ---------------------------------------------------------------------

    public sealed class ScenarioACancelledUpsertDuringAReinstallLeavesThePreviousInstallUntouched
    {
        [Fact]
        public async Task TheUnwindRunsBeforeTheCancellationPropagates()
        {
            // Mirrors Story395_VoicePackInstall.cs's own
            // ACancelledUpsertDuringAReinstallLeavesThePreviousInstallUntouched exactly, one asset
            // kind over — store.ThrowOnUpsert = OperationCanceledException proves
            // JinglePackController.Install's own unwind-then-rethrow discipline (never a 500)
            // without a real Postgres cancellation. A SECOND, fresh factory instance is required for
            // the reinstall attempt to genuinely serve DIFFERENT bytes — CatalogProxyService's own
            // 15-minute index/entry cache would otherwise replay the FIRST install's cached bytes
            // against the SAME URLs.
            const string slug = "cancelled-reinstall-pack";
            const string keptFile = "kept.wav";
            using var jingleRootDir = new TempDir();
            var jingleRoot = jingleRootDir.Path;
            var assetDir = JingleTestAudio.NewTempDir();
            try
            {
                var store = new FakeJinglePackStore();
                string firstHash;

                var firstBedPath = JingleTestAudio.CreateBedShapedTone(assetDir, keptFile);
                var firstAssets = new[] { ("Kept Bed", "bed", keptFile, File.ReadAllBytes(firstBedPath)) };
                using (var firstFactory = new EnrichmentFailureWebFactory(store, slug, firstAssets, jingleRoot))
                {
                    var firstClient = await EnrichmentFailureWebFactory.LoggedInClientAsync(firstFactory);
                    var first = await firstClient.PostAsync($"/api/jingle-packs/{slug}/install", null);
                    Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());

                    firstHash = Convert.ToHexStringLower(
                        SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(jingleRoot, slug, keptFile))));
                }

                store.ThrowOnUpsert = new OperationCanceledException("simulated client disconnect mid-upsert");

                var secondBedPath = JingleTestAudio.CreateBedShapedTone(
                    assetDir, "kept-v2.wav", leadingSilenceSec: 2.0, toneSec: 4.0, trailingSilenceSec: 2.0);
                var secondAssets = new[] { ("Kept Bed", "bed", keptFile, File.ReadAllBytes(secondBedPath)) };
                using (var secondFactory = new EnrichmentFailureWebFactory(store, slug, secondAssets, jingleRoot))
                {
                    var secondClient = await EnrichmentFailureWebFactory.LoggedInClientAsync(secondFactory);
                    try
                    {
                        await secondClient.PostAsync($"/api/jingle-packs/{slug}/install", null);
                    }
                    catch (OperationCanceledException)
                    {
                        // The unhandled cancellation resurfaces at the call site via TestServer —
                        // expected; the fix's own point is that the unwind below already ran.
                    }
                }

                var restoredHash = Convert.ToHexStringLower(
                    SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(jingleRoot, slug, keptFile))));
                Assert.Equal(firstHash, restoredHash);
                Assert.Equal(1, store.PackCount);
                Assert.Empty(Directory.EnumerateFiles(Path.Combine(jingleRoot, slug), "*.prev-*"));
                Assert.Empty(Directory.EnumerateDirectories(jingleRoot, "*.staging-*"));
            }
            finally
            {
                try { Directory.Delete(assetDir, recursive: true); }
                catch (IOException) { /* best-effort cleanup */ }
                catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
            }
        }
    }
}

// ── Arc: fresh install, second asset corrupt ────────────────────────────────────────────────────

/// <summary>Boots one <see cref="EnrichmentFailureWebFactory"/> against a two-asset pack (a real,
/// ffmpeg-generated tone, then <see cref="JingleTestAudio.CreateCorrupt"/>), installs once, and holds
/// the HTTP response + jingle root for every Scenario fact above to read. <see cref="IDisposable"/>
/// disposes the factory and deletes the jingle root — this arc is built fresh per-fact (cheap: no
/// ephemeral Postgres), never shared, so there is nothing to arrange once.</summary>
file sealed class FreshCorruptArc : IDisposable
{
    public const string Slug = "fresh-corrupt-pack";
    const string GoodFile = "good.wav";
    const string CorruptFile = "corrupt.wav";

    public HttpStatusCode Status { get; private init; }
    public string Body { get; private init; } = "";
    public string JingleRoot => jingleRootDir.Path;
    public FakeJinglePackStore Store { get; }

    readonly TempDir jingleRootDir;
    readonly EnrichmentFailureWebFactory factory;

    FreshCorruptArc(TempDir jingleRootDir, FakeJinglePackStore store, EnrichmentFailureWebFactory factory, HttpStatusCode status, string body)
    {
        this.jingleRootDir = jingleRootDir;
        Store = store;
        this.factory = factory;
        Status = status;
        Body = body;
    }

    public static async Task<FreshCorruptArc> RunAsync()
    {
        var jingleRootDir = new TempDir();
        var jingleRoot = jingleRootDir.Path;
        var assetDir = JingleTestAudio.NewTempDir();
        try
        {
            var goodPath = JingleTestAudio.CreateBedShapedTone(assetDir, GoodFile);
            var corruptPath = JingleTestAudio.CreateCorrupt(assetDir, CorruptFile);
            var assets = new[]
            {
                ("Good Bed", "bed", GoodFile, File.ReadAllBytes(goodPath)),
                ("Corrupt Sting", "sting", CorruptFile, File.ReadAllBytes(corruptPath)),
            };

            var store = new FakeJinglePackStore();
            // Pass this arc's own `jingleRoot` explicitly (T414 review round 3 finding 1 — a since-
            // removed overload made its OWN, unrelated temp root, so this arc's own `jingleRoot` above
            // was silently never the one the factory actually installed into, and
            // NothingLandsUnderTheJingleRootForThisSlug/NoStagingSiblingSurvives below were reading an
            // always-empty directory); mirrors ReinstallCorruptArc's/HashMismatchArc's own call exactly.
            var factory = new EnrichmentFailureWebFactory(store, Slug, assets, jingleRoot);
            var client = await EnrichmentFailureWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsync($"/api/jingle-packs/{Slug}/install", null);
            var body = await response.Content.ReadAsStringAsync();

            return new FreshCorruptArc(jingleRootDir, store, factory, response.StatusCode, body);
        }
        catch
        {
            // Construction failed before ownership of jingleRoot passed to the returned instance's own
            // Dispose (the `using` in the calling Scenario never runs when RunAsync itself throws) — clean
            // it up here or it leaks forever (T414 review round 3 finding 5).
            jingleRootDir.Dispose();
            throw;
        }
        finally
        {
            try { Directory.Delete(assetDir, recursive: true); }
            catch (IOException) { /* best-effort cleanup */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
        }
    }

    public void Dispose()
    {
        factory.Dispose();
        jingleRootDir.Dispose();
    }
}

// ── Arc: fresh install, second asset TagLib-unreadable (real FLAC bytes under a .wav name) ─────

/// <summary>Same shape as <see cref="FreshCorruptArc"/> (T414 delta review finding 2) — the SECOND
/// asset is real, ffmpeg-measurable audio that TagLib's own extension-driven parser choice cannot
/// read (<see cref="JingleTestAudio.CreateFlacNamedAsWav"/>'s own remarks), proving the TagLib catch
/// in <c>JinglePackController.StageAndEnrichAllAsync</c> aborts the whole pack on its own, not just
/// the loudness gate a plain-corrupt fixture always trips first.</summary>
file sealed class TagLibUnreadableArc : IDisposable
{
    public const string Slug = "taglib-unreadable-pack";
    const string GoodFile = "good.wav";
    public const string BadFile = "taglib-unreadable.wav";

    public HttpStatusCode Status { get; private init; }
    public string Body { get; private init; } = "";
    public string JingleRoot => jingleRootDir.Path;
    public FakeJinglePackStore Store { get; }

    readonly TempDir jingleRootDir;
    readonly EnrichmentFailureWebFactory factory;

    TagLibUnreadableArc(TempDir jingleRootDir, FakeJinglePackStore store, EnrichmentFailureWebFactory factory, HttpStatusCode status, string body)
    {
        this.jingleRootDir = jingleRootDir;
        Store = store;
        this.factory = factory;
        Status = status;
        Body = body;
    }

    public static async Task<TagLibUnreadableArc> RunAsync()
    {
        var jingleRootDir = new TempDir();
        var jingleRoot = jingleRootDir.Path;
        var assetDir = JingleTestAudio.NewTempDir();
        try
        {
            var goodPath = JingleTestAudio.CreateBedShapedTone(assetDir, GoodFile);
            var badPath = JingleTestAudio.CreateFlacNamedAsWav(assetDir, BadFile);
            var assets = new[]
            {
                ("Good Bed", "bed", GoodFile, File.ReadAllBytes(goodPath)),
                ("Unreadable Sting", "sting", BadFile, File.ReadAllBytes(badPath)),
            };

            var store = new FakeJinglePackStore();
            var factory = new EnrichmentFailureWebFactory(store, Slug, assets, jingleRoot);
            var client = await EnrichmentFailureWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsync($"/api/jingle-packs/{Slug}/install", null);
            var body = await response.Content.ReadAsStringAsync();

            return new TagLibUnreadableArc(jingleRootDir, store, factory, response.StatusCode, body);
        }
        catch
        {
            // Construction failed before ownership of jingleRoot passed to the returned instance's own
            // Dispose (the `using` in the calling Scenario never runs when RunAsync itself throws) — clean
            // it up here or it leaks forever (T414 review round 3 finding 5).
            jingleRootDir.Dispose();
            throw;
        }
        finally
        {
            try { Directory.Delete(assetDir, recursive: true); }
            catch (IOException) { /* best-effort cleanup */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
        }
    }

    public void Dispose()
    {
        factory.Dispose();
        jingleRootDir.Dispose();
    }
}

// ── Arc: fresh install, second asset zero-duration per TagLib (data chunk size zeroed) ──────────

/// <summary>Same shape as <see cref="TagLibUnreadableArc"/> (T414 delta review finding 2) — the
/// SECOND asset is real, ffmpeg-measurable audio whose own <c>data</c> chunk size field is zeroed
/// (<see cref="JingleTestAudio.CreateZeroDurationWav"/>'s own remarks), so TagLib reads it back as
/// zero seconds without throwing — proving the OTHER of the two post-hash enrichment gates
/// (<c>props is not { Duration.TotalSeconds: > 0 }</c>) aborts the whole pack on its own.</summary>
file sealed class ZeroDurationArc : IDisposable
{
    public const string Slug = "zero-duration-pack";
    const string GoodFile = "good.wav";
    public const string BadFile = "zero-duration.wav";

    public HttpStatusCode Status { get; private init; }
    public string Body { get; private init; } = "";
    public string JingleRoot => jingleRootDir.Path;
    public FakeJinglePackStore Store { get; }

    readonly TempDir jingleRootDir;
    readonly EnrichmentFailureWebFactory factory;

    ZeroDurationArc(TempDir jingleRootDir, FakeJinglePackStore store, EnrichmentFailureWebFactory factory, HttpStatusCode status, string body)
    {
        this.jingleRootDir = jingleRootDir;
        Store = store;
        this.factory = factory;
        Status = status;
        Body = body;
    }

    public static async Task<ZeroDurationArc> RunAsync()
    {
        var jingleRootDir = new TempDir();
        var jingleRoot = jingleRootDir.Path;
        var assetDir = JingleTestAudio.NewTempDir();
        try
        {
            var goodPath = JingleTestAudio.CreateBedShapedTone(assetDir, GoodFile);
            var badPath = JingleTestAudio.CreateZeroDurationWav(assetDir, BadFile);
            var assets = new[]
            {
                ("Good Bed", "bed", GoodFile, File.ReadAllBytes(goodPath)),
                ("Zero Duration Sting", "sting", BadFile, File.ReadAllBytes(badPath)),
            };

            var store = new FakeJinglePackStore();
            var factory = new EnrichmentFailureWebFactory(store, Slug, assets, jingleRoot);
            var client = await EnrichmentFailureWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsync($"/api/jingle-packs/{Slug}/install", null);
            var body = await response.Content.ReadAsStringAsync();

            return new ZeroDurationArc(jingleRootDir, store, factory, response.StatusCode, body);
        }
        catch
        {
            // Construction failed before ownership of jingleRoot passed to the returned instance's own
            // Dispose (the `using` in the calling Scenario never runs when RunAsync itself throws) — clean
            // it up here or it leaks forever (T414 review round 3 finding 5).
            jingleRootDir.Dispose();
            throw;
        }
        finally
        {
            try { Directory.Delete(assetDir, recursive: true); }
            catch (IOException) { /* best-effort cleanup */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
        }
    }

    public void Dispose()
    {
        factory.Dispose();
        jingleRootDir.Dispose();
    }
}

// ── Arc: a good first install, then a reinstall whose second asset is corrupt ───────────────────

file sealed class ReinstallCorruptArc : IDisposable
{
    public const string Slug = "reinstall-corrupt-pack";
    public const string KeptFile = "kept.wav";
    public const string OtherFile = "other.wav";

    public HttpStatusCode SecondStatus { get; private init; }
    public string JingleRoot => jingleRootDir.Path;
    public FakeJinglePackStore Store { get; }
    public string FirstKeptBytesHash { get; private init; } = "";
    public string FirstOtherBytesHash { get; private init; } = "";

    readonly TempDir jingleRootDir;
    readonly EnrichmentFailureWebFactory firstFactory;
    readonly EnrichmentFailureWebFactory secondFactory;

    ReinstallCorruptArc(
        TempDir jingleRootDir, FakeJinglePackStore store, EnrichmentFailureWebFactory firstFactory, EnrichmentFailureWebFactory secondFactory,
        HttpStatusCode secondStatus, string firstKeptHash, string firstOtherHash)
    {
        this.jingleRootDir = jingleRootDir;
        Store = store;
        this.firstFactory = firstFactory;
        this.secondFactory = secondFactory;
        SecondStatus = secondStatus;
        FirstKeptBytesHash = firstKeptHash;
        FirstOtherBytesHash = firstOtherHash;
    }

    public static async Task<ReinstallCorruptArc> RunAsync()
    {
        var jingleRootDir = new TempDir();
        var jingleRoot = jingleRootDir.Path;
        var assetDir = JingleTestAudio.NewTempDir();
        try
        {
            var keptPath = JingleTestAudio.CreateBedShapedTone(assetDir, KeptFile);
            var otherPath = JingleTestAudio.CreateBedShapedTone(assetDir, OtherFile, leadingSilenceSec: 1.5, toneSec: 2.5, trailingSilenceSec: 1.5);
            var firstAssets = new[]
            {
                ("Kept Bed", "bed", KeptFile, File.ReadAllBytes(keptPath)),
                ("Other Sting", "sting", OtherFile, File.ReadAllBytes(otherPath)),
            };

            var store = new FakeJinglePackStore();
            var firstFactory = new EnrichmentFailureWebFactory(store, Slug, firstAssets, jingleRoot);
            var firstClient = await EnrichmentFailureWebFactory.LoggedInClientAsync(firstFactory);
            var firstInstall = await firstClient.PostAsync($"/api/jingle-packs/{Slug}/install", null);
            if (!firstInstall.IsSuccessStatusCode)
                throw new InvalidOperationException($"fixture first install failed: {await firstInstall.Content.ReadAsStringAsync()}");

            var firstKeptHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(jingleRoot, Slug, KeptFile))));
            var firstOtherHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(jingleRoot, Slug, OtherFile))));

            // Second attempt: "kept" carries brand NEW (still valid) bytes — reinstalling it is
            // genuinely attempted — but "other" is now corrupt, so the whole attempt must abort
            // before either file is ever moved into place (StageAndEnrichAllAsync fails asset-by-
            // asset, entirely before MoveStagedFilesIntoPlaceAsync ever runs for this attempt).
            var newKeptPath = JingleTestAudio.CreateBedShapedTone(assetDir, "kept-v2.wav", leadingSilenceSec: 2.0, toneSec: 4.0, trailingSilenceSec: 2.0);
            var corruptOtherPath = JingleTestAudio.CreateCorrupt(assetDir, "other-corrupt.wav");
            var secondAssets = new[]
            {
                ("Kept Bed", "bed", KeptFile, File.ReadAllBytes(newKeptPath)),
                ("Other Sting", "sting", OtherFile, File.ReadAllBytes(corruptOtherPath)),
            };

            var secondFactory = new EnrichmentFailureWebFactory(store, Slug, secondAssets, jingleRoot);
            var secondClient = await EnrichmentFailureWebFactory.LoggedInClientAsync(secondFactory);
            var secondInstall = await secondClient.PostAsync($"/api/jingle-packs/{Slug}/install", null);

            return new ReinstallCorruptArc(
                jingleRootDir, store, firstFactory, secondFactory, secondInstall.StatusCode, firstKeptHash, firstOtherHash);
        }
        catch
        {
            // Construction failed before ownership of jingleRoot passed to the returned instance's own
            // Dispose (the `using` in the calling Scenario never runs when RunAsync itself throws) — clean
            // it up here or it leaks forever (T414 review round 3 finding 5).
            jingleRootDir.Dispose();
            throw;
        }
        finally
        {
            try { Directory.Delete(assetDir, recursive: true); }
            catch (IOException) { /* best-effort cleanup */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
        }
    }

    public void Dispose()
    {
        firstFactory.Dispose();
        secondFactory.Dispose();
        jingleRootDir.Dispose();
    }
}

// ── Arc: one asset's declared sha256 does not match its actual served bytes ─────────────────────

file sealed class HashMismatchArc : IDisposable
{
    public const string Slug = "hash-mismatch-pack";
    const string File1 = "asset.wav";

    public HttpStatusCode Status { get; private init; }
    public string JingleRoot => jingleRootDir.Path;
    public FakeJinglePackStore Store { get; }

    readonly TempDir jingleRootDir;
    readonly HashMismatchWebFactory factory;

    HashMismatchArc(TempDir jingleRootDir, FakeJinglePackStore store, HashMismatchWebFactory factory, HttpStatusCode status)
    {
        this.jingleRootDir = jingleRootDir;
        Store = store;
        this.factory = factory;
        Status = status;
    }

    public static async Task<HashMismatchArc> RunAsync()
    {
        var jingleRootDir = new TempDir();
        var jingleRoot = jingleRootDir.Path;
        var assetDir = JingleTestAudio.NewTempDir();
        try
        {
            var path = JingleTestAudio.CreateTone(assetDir, File1);
            var bytes = File.ReadAllBytes(path);

            var store = new FakeJinglePackStore();
            var factory = new HashMismatchWebFactory(store, Slug, File1, bytes, jingleRoot);
            var client = await HashMismatchWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsync($"/api/jingle-packs/{Slug}/install", null);
            return new HashMismatchArc(jingleRootDir, store, factory, response.StatusCode);
        }
        catch
        {
            // Construction failed before ownership of jingleRoot passed to the returned instance's own
            // Dispose (the `using` in the calling Scenario never runs when RunAsync itself throws) — clean
            // it up here or it leaks forever (T414 review round 3 finding 5).
            jingleRootDir.Dispose();
            throw;
        }
        finally
        {
            try { Directory.Delete(assetDir, recursive: true); }
            catch (IOException) { /* best-effort cleanup */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
        }
    }

    public void Dispose()
    {
        factory.Dispose();
        jingleRootDir.Dispose();
    }
}

// ── Arc: a manifest asset `file` attempts a path escape ("../escape.wav") ───────────────────────

file sealed class PathEscapeArc : IDisposable
{
    public const string Slug = "path-escape-pack";

    public HttpStatusCode Status { get; private init; }
    public string Body { get; private init; } = "";
    public string JingleRoot => jingleRootDir.Path;
    public FakeJinglePackStore Store { get; }

    readonly TempDir jingleRootDir;
    readonly PathEscapeWebFactory factory;

    PathEscapeArc(TempDir jingleRootDir, FakeJinglePackStore store, PathEscapeWebFactory factory, HttpStatusCode status, string body)
    {
        this.jingleRootDir = jingleRootDir;
        Store = store;
        this.factory = factory;
        Status = status;
        Body = body;
    }

    public static async Task<PathEscapeArc> RunAsync()
    {
        var jingleRootDir = new TempDir();
        var jingleRoot = jingleRootDir.Path;
        try
        {
            var store = new FakeJinglePackStore();
            var factory = new PathEscapeWebFactory(store, Slug, jingleRoot);
            var client = await PathEscapeWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsync($"/api/jingle-packs/{Slug}/install", null);
            var body = await response.Content.ReadAsStringAsync();

            return new PathEscapeArc(jingleRootDir, store, factory, response.StatusCode, body);
        }
        catch
        {
            // Construction failed before ownership of jingleRoot passed to the returned instance's own
            // Dispose (the `using` in the calling Scenario never runs when RunAsync itself throws) — clean
            // it up here or it leaks forever (T414 review round 3 finding 5).
            jingleRootDir.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        factory.Dispose();
        jingleRootDir.Dispose();
    }
}

// ── Test harnesses ───────────────────────────────────────────────────────────────────────────────

/// <summary>Fake ads-library resolver — <see cref="AdsOptions.LibraryName"/>'s own default ("ads")
/// resolves to a fixed id, no real Postgres. Every other member throws
/// <see cref="NotSupportedException"/> — deliberately narrower than <see cref="ILibraryRepository"/>'s
/// own full seam (mirrors the "file sealed class FakeLibraryRepository" idiom already used per-file
/// across this test project — e.g. Story047_LibraryCrudEndpoints.cs, Story313_SafeSegmentShowScope.cs
/// — never called by anything this file's own Scenarios exercise).</summary>
file sealed class FakeAdsLibraryRepository : ILibraryRepository
{
    public Task<LibraryAdminInfo?> GetByNameAsync(string name, CancellationToken ct) =>
        Task.FromResult<LibraryAdminInfo?>(new LibraryAdminInfo(Id: 1, Name: name, MediaCount: 0));

    public Task<IReadOnlyList<LibraryInfo>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) =>
        throw new NotSupportedException("not exercised by this file's own Scenarios");

    public Task<IReadOnlyList<LibraryAdminInfo>> GetAllWithMediaCountAsync(CancellationToken ct) =>
        throw new NotSupportedException("not exercised by this file's own Scenarios");
}

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> serving a single-slug fake catalog origin whose
/// manifest names every <paramref name="assets"/> entry — real, ffmpeg-generated (or deliberately
/// corrupt) bytes, so the REAL <c>ILoudnessAnalyzer</c>/<c>ICueAnalyzer</c>/TagLib pipeline is what
/// actually refuses a corrupt one, never a faked outcome. <see cref="IJinglePackStore"/> and
/// <see cref="ILibraryRepository"/> are both swapped for fakes (no <c>ConnectionStrings:Library</c>
/// ever dialed) — this file's whole point is the CONTROLLER's own abort discipline, not the store's.
/// </summary>
file sealed class EnrichmentFailureWebFactory : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-t414-r2-f1";

    readonly FakeJinglePackStore store;
    readonly FakeHttpMessageHandler handler;

    public string JingleRoot { get; }

    public EnrichmentFailureWebFactory(
        FakeJinglePackStore store, string slug, (string Title, string Role, string File, byte[] Bytes)[] assets, string jingleRoot)
    {
        this.store = store;
        JingleRoot = jingleRoot;
        handler = JinglePackSadPathFixtures.BuildRoutedHandler(slug, assets);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Community:CatalogIndexUrl", JinglePackSadPathFixtures.IndexUrl);
        builder.UseSetting("Packs:JingleRoot", JingleRoot);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(handler));

            services.RemoveAll<IJinglePackStore>();
            services.AddSingleton<IJinglePackStore>(store);
            services.RemoveAll<ILibraryRepository>();
            services.AddSingleton<ILibraryRepository, FakeAdsLibraryRepository>();
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

/// <summary>Same shape as <see cref="EnrichmentFailureWebFactory"/>, one asset, whose manifest-pinned
/// sha256 deliberately does NOT match the bytes the fake origin actually serves (F6.1) — a distinct
/// factory rather than a parameter on the one above, since the fixture builder needs a genuinely wrong
/// hash rather than the real one <see cref="JinglePackSadPathFixtures.BuildRoutedHandler"/> always
/// computes off the real bytes.</summary>
file sealed class HashMismatchWebFactory : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-t414-r2-f61";

    readonly FakeJinglePackStore store;
    readonly FakeHttpMessageHandler handler;
    readonly string jingleRoot;

    public HashMismatchWebFactory(FakeJinglePackStore store, string slug, string file, byte[] bytes, string jingleRoot)
    {
        this.store = store;
        this.jingleRoot = jingleRoot;
        handler = JinglePackSadPathFixtures.BuildHashMismatchHandler(slug, file, bytes);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Community:CatalogIndexUrl", JinglePackSadPathFixtures.IndexUrl);
        builder.UseSetting("Packs:JingleRoot", jingleRoot);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(handler));

            services.RemoveAll<IJinglePackStore>();
            services.AddSingleton<IJinglePackStore>(store);
            services.RemoveAll<ILibraryRepository>();
            services.AddSingleton<ILibraryRepository, FakeAdsLibraryRepository>();
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

/// <summary>Same shape as <see cref="EnrichmentFailureWebFactory"/>, whose ONE manifest asset declares
/// a path-escaping <c>file</c> (Advisory 3, T414 review round 3) — a distinct factory since the fixture
/// needs the manifest and the index's own asset entry to deliberately DISAGREE (see
/// <see cref="JinglePackSadPathFixtures.BuildPathEscapeHandler"/>'s own remarks for why).</summary>
file sealed class PathEscapeWebFactory : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-t414-r3-pathescape";

    readonly FakeJinglePackStore store;
    readonly FakeHttpMessageHandler handler;
    readonly string jingleRoot;

    public PathEscapeWebFactory(FakeJinglePackStore store, string slug, string jingleRoot)
    {
        this.store = store;
        this.jingleRoot = jingleRoot;
        handler = JinglePackSadPathFixtures.BuildPathEscapeHandler(slug);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Community:CatalogIndexUrl", JinglePackSadPathFixtures.IndexUrl);
        builder.UseSetting("Packs:JingleRoot", jingleRoot);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(handler));

            services.RemoveAll<IJinglePackStore>();
            services.AddSingleton<IJinglePackStore>(store);
            services.RemoveAll<ILibraryRepository>();
            services.AddSingleton<ILibraryRepository, FakeAdsLibraryRepository>();
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

/// <summary>Builds a one-slug fake catalog origin's index/manifest/meta/asset bodies for this file's
/// own three arcs — mirrors <c>Story399_JinglePackInstall.cs</c>'s own
/// <c>JinglePackInstallFixtures</c> idiom, parameterized over an arbitrary asset list per call rather
/// than one fixed pack, since each Scenario here needs its own distinct bytes.</summary>
file static class JinglePackSadPathFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/jingle-sadpath-index.json";
    const string Origin = "https://catalog.test/repo/";
    const string PackName = "Sad Path Test Pack";

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static string ManifestJson(string slug, (string Title, string Role, string File, byte[] Bytes)[] assets) => $$"""
        { "packName": "{{PackName}}",
          "assets": [
            {{string.Join(", ", assets.Select(a =>
                $$"""{ "file": "{{a.File}}", "sha256": "{{Sha256Hex(a.Bytes)}}", "role": "{{a.Role}}", "title": "{{a.Title}}", "license": "CC0" }"""))}}
          ] }
        """;

    const string MetaJson = """
        {"author":"Test Fixture","description":"A jingle pack for the sad-path specs.","audience":"everyone"}
        """;

    static string EntryJson(string slug, string manifestJson, (string Title, string Role, string File, byte[] Bytes)[] assets) => $$"""
        { "slug": "{{slug}}", "kind": "jingle-pack", "audience": "everyone",
          "manifest": { "path": "entries/{{slug}}/{{slug}}.jingle-pack.json", "sha256": "{{Sha256Hex(manifestJson)}}" },
          "meta": { "path": "entries/{{slug}}/{{slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
          "assets": [
            {{string.Join(", ", assets.Select(a =>
                $$"""{ "path": "entries/{{slug}}/{{a.File}}", "sha256": "{{Sha256Hex(a.Bytes)}}", "bytes": {{a.Bytes.Length}} }"""))}}
          ] }
        """;

    static string IndexJson(string entryJson) => $$"""
        { "generatedAt": "2026-09-07", "entries": [ {{entryJson}} ] }
        """;

    public static FakeHttpMessageHandler BuildRoutedHandler(string slug, (string Title, string Role, string File, byte[] Bytes)[] assets)
    {
        var manifestJson = ManifestJson(slug, assets);
        var entryJson = EntryJson(slug, manifestJson, assets);
        var indexJson = IndexJson(entryJson);

        var routes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [IndexUrl] = indexJson,
            [Origin + $"entries/{slug}/{slug}.jingle-pack.json"] = manifestJson,
            [Origin + $"entries/{slug}/{slug}.meta.json"] = MetaJson,
        };
        var assetBytesByUrl = assets.ToDictionary(a => Origin + $"entries/{slug}/{a.File}", a => a.Bytes, StringComparer.Ordinal);

        return BuildHandler(routes, assetBytesByUrl);
    }

    /// <summary>F6.1 — the index/entry declare the asset's REAL sha256 (so the ENTRY's own hash-verify
    /// passes cleanly), but the asset route itself serves DIFFERENT bytes than
    /// <paramref name="bytes"/> — the exact shape that trips
    /// <c>CatalogInstallShell.FetchAllAssetsUncachedAsync</c>'s own per-asset hash-verify, distinct
    /// from a manifest/meta-level mismatch.</summary>
    public static FakeHttpMessageHandler BuildHashMismatchHandler(string slug, string file, byte[] bytes)
    {
        var assets = new[] { ("Mismatched Asset", "bed", file, bytes) };
        var manifestJson = ManifestJson(slug, assets);
        var entryJson = EntryJson(slug, manifestJson, assets);
        var indexJson = IndexJson(entryJson);

        var routes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [IndexUrl] = indexJson,
            [Origin + $"entries/{slug}/{slug}.jingle-pack.json"] = manifestJson,
            [Origin + $"entries/{slug}/{slug}.meta.json"] = MetaJson,
        };
        // Deliberately WRONG bytes at the asset route — the index/manifest above both pin the sha256
        // of the ORIGINAL bytes, so this is what actually fails the per-asset hash-verify.
        var wrongBytes = Encoding.UTF8.GetBytes("these bytes do not match the manifest's own pinned sha256");
        var assetBytesByUrl = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [Origin + $"entries/{slug}/{file}"] = wrongBytes,
        };

        return BuildHandler(routes, assetBytesByUrl);
    }

    /// <summary>Advisory 3 (T414 review round 3) — the MANIFEST's own asset <c>file</c> is
    /// <c>"../escape.wav"</c> (a path-escape attempt), while the INDEX's own <c>assets[]</c> entry for
    /// this slug stays a perfectly ordinary, valid file. <c>CatalogIndexValidator</c>'s own
    /// asset-path pattern would refuse the SAME string just as absolutely if it ever appeared THERE
    /// too (a different layer, already its own test surface, not this controller's — and refusing it
    /// there would only skip the whole entry, masking the fact this file means to pin behind an
    /// unrelated "not found") — keeping the index-level entry valid is what actually reaches the
    /// MANIFEST parser's own anchored <c>FileFormat</c> gate.</summary>
    public static FakeHttpMessageHandler BuildPathEscapeHandler(string slug)
    {
        (string Title, string Role, string File, byte[] Bytes)[] indexAssets =
            [("Decoy Asset", "bed", "decoy.wav", Encoding.UTF8.GetBytes("decoy bytes, never fetched"))];
        (string Title, string Role, string File, byte[] Bytes)[] manifestAssets =
            [("Escape Asset", "bed", "../escape.wav", Encoding.UTF8.GetBytes("never fetched either"))];

        var manifestJson = ManifestJson(slug, manifestAssets);
        var entryJson = EntryJson(slug, manifestJson, indexAssets);
        var indexJson = IndexJson(entryJson);

        var routes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [IndexUrl] = indexJson,
            [Origin + $"entries/{slug}/{slug}.jingle-pack.json"] = manifestJson,
            [Origin + $"entries/{slug}/{slug}.meta.json"] = MetaJson,
        };
        var assetBytesByUrl = indexAssets.ToDictionary(a => Origin + $"entries/{slug}/{a.File}", a => a.Bytes, StringComparer.Ordinal);

        return BuildHandler(routes, assetBytesByUrl);
    }

    static FakeHttpMessageHandler BuildHandler(Dictionary<string, string> routes, Dictionary<string, byte[]> assetBytesByUrl) =>
        new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;

            if (assetBytesByUrl.TryGetValue(absoluteUri, out var assetBytes))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(assetBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
}
