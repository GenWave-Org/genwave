// STORY-429 — Preview files clean up automatically (SPEC F174.9 · PLAN T442)

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Fakes;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

public static class FeaturePreviewFilesCleanUpAutomatically
{
    static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A real, writable stand-in for <see cref="AdSpotLocatorRoots.AuthoredRoot"/> — AC2/AC3
    /// assert an actual file disappears from disk, so the guardian's own <c>SweepPreviewsAsync</c> needs
    /// a root it can genuinely delete under, not the harness's placeholder <c>/authored</c> path (never
    /// writable in this sandbox). <see langword="using"/>-disposed (implicitly converts to
    /// <see langword="string"/> at every call site below) so this file stops leaking one temp directory
    /// per fact.</summary>
    static AuthoredRootScope NewAuthoredRoot(string prefix) => new(Directory.CreateTempSubdirectory(prefix).FullName);

    static string SeedPreviewFile(string authoredRoot, string fileName)
    {
        var previewDir = Directory.CreateDirectory(Path.Combine(authoredRoot, "preview")).FullName;
        var path = Path.Combine(previewDir, fileName);
        File.WriteAllBytes(path, "RIFF"u8.ToArray());
        return path;
    }

    static (FakeAdSpotLifecycleStore Store, AdSpotLifecycleGuardianService Guardian) BuildGuardian(
        string authoredRoot, int previewRetentionDays = 7, ILogger<AdSpotLifecycleGuardianService>? logger = null)
    {
        var store = new FakeAdSpotLifecycleStore();
        var adsOptions = new FakeOptionsMonitor<AdsOptions>(new AdsOptions { PreviewRetentionDays = previewRetentionDays });
        var locatorRoots = new AdSpotLocatorRoots("/media", authoredRoot);
        var guardian = new AdSpotLifecycleGuardianService(
            store, adsOptions, locatorRoots, new FakeTimeProvider(Now), logger ?? new NoOpLogger<AdSpotLifecycleGuardianService>());
        return (store, guardian);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioPromotionMovesTheFileNotCopies
    {
        [Fact]
        public void ThePreviewPathIsGoneAfterPromotion()
            => Assert.Fail("pending: T445 move not copy — AC1");

        [Fact]
        public void ThePromotedFileExists()
            => Assert.Fail("pending: T445 move not copy — AC1");
    }

    public sealed class ScenarioAReadyOrRetiredSpotsPreviewIsDeletedOnTheGuardianTick
    {
        [Fact]
        public async Task TheFileIsDeleted()
        {
            // Given a spot in state 'ready' with preview_path set, its preview rendered only an hour
            // ago — well inside the retention window, so ONLY the state-based branch can be why this
            // sweeps.
            using var authoredRoot = NewAuthoredRoot("t442-preview-ac2-");
            var previewPath = SeedPreviewFile(authoredRoot, "42-deadbeef.wav");
            var (store, guardian) = BuildGuardian(authoredRoot);
            store.AddExisting(new AdSpot(
                Id: 42, SponsorId: 1, SponsorName: "Acme", Title: "Acme spot", Brief: null,
                Script: "ANNOUNCER: Hi.", Source: AdSource.Llm, PackSlug: null, SpotSeconds: 30,
                VoicePlan: null, BedMediaId: null, State: AdState.Ready, FailReason: null, MediaId: 900,
                Generation: 1, CreatedAt: Now.UtcDateTime, StateChangedAt: Now.UtcDateTime,
                RenderedAt: Now.UtcDateTime, RetiredAt: null, Version: "1",
                PreviewPath: previewPath, PreviewAt: Now.UtcDateTime.AddHours(-1), PreviewKey: "deadbeef"));

            // When the guardian ticks...
            await guardian.SweepOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the file is gone.
            Assert.False(File.Exists(previewPath));
        }

        [Fact]
        public async Task ThePreviewStampsAreCleared()
        {
            using var authoredRoot = NewAuthoredRoot("t442-preview-ac2-");
            var previewPath = SeedPreviewFile(authoredRoot, "42-deadbeef.wav");
            var (store, guardian) = BuildGuardian(authoredRoot);
            store.AddExisting(new AdSpot(
                Id: 42, SponsorId: 1, SponsorName: "Acme", Title: "Acme spot", Brief: null,
                Script: "ANNOUNCER: Hi.", Source: AdSource.Llm, PackSlug: null, SpotSeconds: 30,
                VoicePlan: null, BedMediaId: null, State: AdState.Ready, FailReason: null, MediaId: 900,
                Generation: 1, CreatedAt: Now.UtcDateTime, StateChangedAt: Now.UtcDateTime,
                RenderedAt: Now.UtcDateTime, RetiredAt: null, Version: "1",
                PreviewPath: previewPath, PreviewAt: Now.UtcDateTime.AddHours(-1), PreviewKey: "deadbeef"));

            await guardian.SweepOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            var swept = await store.GetByIdAsync(42, CancellationToken.None);
            Assert.NotNull(swept);
            Assert.Null(swept.PreviewPath);
            Assert.Null(swept.PreviewAt);
            Assert.Null(swept.PreviewKey);
        }
    }

    public sealed class ScenarioRetentionWindowPrunesOldPreviewFiles
    {
        [Fact]
        public async Task AFileOlderThanPreviewRetentionDaysIsDeleted()
        {
            // Given a preview file older than Ads:PreviewRetentionDays (7 here) whose spot is 'draft' —
            // draft is neither ready nor retired, so ONLY the age branch can be why this sweeps.
            using var authoredRoot = NewAuthoredRoot("t442-preview-ac3-");
            var previewPath = SeedPreviewFile(authoredRoot, "7-cafef00d.wav");
            var (store, guardian) = BuildGuardian(authoredRoot, previewRetentionDays: 7);
            store.AddExisting(new AdSpot(
                Id: 7, SponsorId: 1, SponsorName: "Acme", Title: "Acme spot", Brief: null,
                Script: "ANNOUNCER: Hi.", Source: AdSource.Llm, PackSlug: null, SpotSeconds: 30,
                VoicePlan: null, BedMediaId: null, State: AdState.Draft, FailReason: null, MediaId: null,
                Generation: 1, CreatedAt: Now.UtcDateTime.AddDays(-8), StateChangedAt: Now.UtcDateTime.AddDays(-8),
                RenderedAt: null, RetiredAt: null, Version: "1",
                PreviewPath: previewPath, PreviewAt: Now.UtcDateTime.AddDays(-8), PreviewKey: "cafef00d"));

            // When the guardian ticks...
            await guardian.SweepOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the file is gone.
            Assert.False(File.Exists(previewPath));
        }

        [Fact]
        public async Task TheRowsPreviewStampsAreCleared()
        {
            using var authoredRoot = NewAuthoredRoot("t442-preview-ac3-");
            var previewPath = SeedPreviewFile(authoredRoot, "7-cafef00d.wav");
            var (store, guardian) = BuildGuardian(authoredRoot, previewRetentionDays: 7);
            store.AddExisting(new AdSpot(
                Id: 7, SponsorId: 1, SponsorName: "Acme", Title: "Acme spot", Brief: null,
                Script: "ANNOUNCER: Hi.", Source: AdSource.Llm, PackSlug: null, SpotSeconds: 30,
                VoicePlan: null, BedMediaId: null, State: AdState.Draft, FailReason: null, MediaId: null,
                Generation: 1, CreatedAt: Now.UtcDateTime.AddDays(-8), StateChangedAt: Now.UtcDateTime.AddDays(-8),
                RenderedAt: null, RetiredAt: null, Version: "1",
                PreviewPath: previewPath, PreviewAt: Now.UtcDateTime.AddDays(-8), PreviewKey: "cafef00d"));

            await guardian.SweepOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            var swept = await store.GetByIdAsync(7, CancellationToken.None);
            Assert.NotNull(swept);
            Assert.Null(swept.PreviewPath);
            Assert.Null(swept.PreviewAt);
            Assert.Null(swept.PreviewKey);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated) — the guardian's own non-happy branches of
    // SweepPreviewsAsync. Both a delete that throws and a path that resolves outside the preview
    // root still clear the row's own preview stamps (never leave a dangling reference forever); a
    // Warning line is logged either way, naming only the spot id.
    // ---------------------------------------------------------------------

    public sealed class ScenarioADeleteFailureStillClearsTheStamps
    {
        [Fact]
        public async Task ADeleteFailureStillClearsTheStamps()
        {
            // The stamped preview_path names a DIRECTORY, not a file — File.Delete throws
            // UnauthorizedAccessException against a directory on Linux, landing in
            // SweepPreviewsAsync's own `IOException or UnauthorizedAccessException` catch.
            using var authoredRoot = NewAuthoredRoot("t442-preview-delfail-");
            var previewDir = Directory.CreateDirectory(Path.Combine(authoredRoot, "preview")).FullName;
            var previewPath = Directory.CreateDirectory(Path.Combine(previewDir, "42-deadbeef.wav")).FullName;
            var logger = new CapturingLogger<AdSpotLifecycleGuardianService>();
            var (store, guardian) = BuildGuardian(authoredRoot, logger: logger);
            store.AddExisting(new AdSpot(
                Id: 42, SponsorId: 1, SponsorName: "Acme", Title: "Acme spot", Brief: null,
                Script: "ANNOUNCER: Hi.", Source: AdSource.Llm, PackSlug: null, SpotSeconds: 30,
                VoicePlan: null, BedMediaId: null, State: AdState.Ready, FailReason: null, MediaId: 900,
                Generation: 1, CreatedAt: Now.UtcDateTime, StateChangedAt: Now.UtcDateTime,
                RenderedAt: Now.UtcDateTime, RetiredAt: null, Version: "1",
                PreviewPath: previewPath, PreviewAt: Now.UtcDateTime.AddHours(-1), PreviewKey: "deadbeef"));

            await guardian.SweepOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Contains(logger.Warnings, w => w.Contains("spot 42", StringComparison.Ordinal));

            var swept = await store.GetByIdAsync(42, CancellationToken.None);
            Assert.NotNull(swept);
            Assert.Null(swept.PreviewPath);
            Assert.Null(swept.PreviewAt);
            Assert.Null(swept.PreviewKey);
        }
    }

    public sealed class ScenarioAnEscapedPathIsSkippedAndItsStampsAreCleared
    {
        [Fact]
        public async Task AnEscapedPathIsSkippedAndItsStampsAreCleared()
        {
            using var authoredRoot = NewAuthoredRoot("t442-preview-escape-");
            // A REAL file genuinely OUTSIDE this authoredRoot's own preview root — proves the
            // guardian truly SKIPS it (never calls File.Delete at all) rather than merely finding
            // nothing there to delete.
            using var outsideRoot = new AuthoredRootScope(Directory.CreateTempSubdirectory("t442-preview-escape-outside-").FullName);
            var escapedPath = Path.Combine(outsideRoot, "escape.wav");
            File.WriteAllBytes(escapedPath, "RIFF"u8.ToArray());
            var logger = new CapturingLogger<AdSpotLifecycleGuardianService>();
            var (store, guardian) = BuildGuardian(authoredRoot, logger: logger);
            store.AddExisting(new AdSpot(
                Id: 43, SponsorId: 1, SponsorName: "Acme", Title: "Acme spot", Brief: null,
                Script: "ANNOUNCER: Hi.", Source: AdSource.Llm, PackSlug: null, SpotSeconds: 30,
                VoicePlan: null, BedMediaId: null, State: AdState.Ready, FailReason: null, MediaId: 900,
                Generation: 1, CreatedAt: Now.UtcDateTime, StateChangedAt: Now.UtcDateTime,
                RenderedAt: Now.UtcDateTime, RetiredAt: null, Version: "1",
                PreviewPath: escapedPath, PreviewAt: Now.UtcDateTime.AddHours(-1), PreviewKey: "escapedkey"));

            await guardian.SweepOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(File.Exists(escapedPath), "the guardian deleted a file outside its own preview root");
            Assert.Contains(
                logger.Warnings,
                w => w.Contains("spot 43", StringComparison.Ordinal) && !w.Contains(escapedPath, StringComparison.Ordinal));

            var swept = await store.GetByIdAsync(43, CancellationToken.None);
            Assert.NotNull(swept);
            Assert.Null(swept.PreviewPath);
            Assert.Null(swept.PreviewAt);
            Assert.Null(swept.PreviewKey);
        }
    }
}

/// <summary>A <see langword="using"/>-disposable wrapper around one
/// <see cref="Directory.CreateTempSubdirectory(string?)"/> call, deleted on <see cref="Dispose"/>
/// (swallowing only <see cref="IOException"/> — anything else is a genuine test bug, not a benign
/// double-delete race). Implicitly converts to <see langword="string"/> so every existing call site
/// that passed a bare path keeps compiling unchanged.</summary>
sealed class AuthoredRootScope(string path) : IDisposable
{
    public string Path { get; } = path;

    public static implicit operator string(AuthoredRootScope scope) => scope.Path;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup only — a leftover temp directory under the OS temp root is never
            // this suite's own claim to prove, and a locked-file race here must never fail a fact.
        }
    }
}
