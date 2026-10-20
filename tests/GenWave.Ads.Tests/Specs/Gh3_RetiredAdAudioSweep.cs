// gh-#3 — retired ad audio is deleted, not kept forever. AdAudioSweepService deletes the file and row
// of ineligible ads-library audio no spot still names, and preview files no spot has stamped once they
// outlive PreviewRetentionDays. Safe segments and jingle packs are never touched: only the ads library
// is listed. Driven through SweepOnceAsync over fakes and a real temp authored root.

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Fakes;
using GenWave.Core.Domain;
using Microsoft.Extensions.Time.Testing;

public static class FeatureRetiredAdAudioSweep
{
    sealed class Rig : IDisposable
    {
        public readonly string Root = Directory.CreateTempSubdirectory("gh3-").FullName;
        public readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
        public readonly FakeAuthoredMediaSweep Media = new();
        public readonly FakeAdAudioReferences References = new();
        public readonly FakeAdsLibraryStore Libraries = new();
        public readonly AdAudioSweepService Service;

        public Rig(bool withAdsLibrary = true)
        {
            Libraries.AddExisting("default");
            if (withAdsLibrary)
                Libraries.AddExisting("ads");
            Service = new AdAudioSweepService(
                Media, References, Libraries,
                new FakeOptionsMonitor<AdsOptions>(new AdsOptions { LibraryName = "ads", PreviewRetentionDays = 7 }),
                new AdSpotLocatorRoots("/media", Root), new FakeTimeProvider(Now),
                new NoOpLogger<AdAudioSweepService>());
        }

        public string File(string dir, string name, int ageDays = 0)
        {
            var path = Path.Combine(Directory.CreateDirectory(Path.Combine(Root, dir)).FullName, name);
            System.IO.File.WriteAllBytes(path, "RIFF"u8.ToArray());
            System.IO.File.SetLastWriteTimeUtc(path, Now.UtcDateTime.AddDays(-ageDays));
            return path;
        }

        public Task SweepAsync() => Service.SweepOnceAsync(CancellationToken.None);

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    public sealed class ScenarioRetiredAudioNoSpotUses
    {
        [Fact]
        public async Task ItsFileAndRowAreDeleted()
        {
            using var rig = new Rig();
            var path = rig.File("ads", "old.wav");
            rig.Media.Rows.Add(new AuthoredMediaFile(18279, path));

            await rig.SweepAsync();

            Assert.False(File.Exists(path));
            Assert.Equal([18279L], rig.Media.Deleted);
        }

        [Fact]
        public async Task OnlyTheAdsLibraryIsListed()
        {
            using var rig = new Rig();

            await rig.SweepAsync();

            Assert.Equal((await rig.Libraries.GetByNameAsync("ads", CancellationToken.None))!.Id, rig.Media.ListedLibraryId);
        }

        [Fact]
        public async Task AFileAlreadyGoneStillLosesItsRow()
        {
            using var rig = new Rig();
            rig.Media.Rows.Add(new AuthoredMediaFile(18308, Path.Combine(rig.Root, "ads", "missing.wav")));

            await rig.SweepAsync();

            Assert.Equal([18308L], rig.Media.Deleted);
        }
    }

    public sealed class ScenarioAudioASpotStillUses
    {
        [Fact]
        public async Task IsKept()
        {
            // Sad path: a recently retired spot, a bed, or a pending swap still names it
            using var rig = new Rig();
            var path = rig.File("ads", "kept.wav");
            rig.Media.Rows.Add(new AuthoredMediaFile(42, path));
            rig.References.InUse.Add(42);

            await rig.SweepAsync();

            Assert.True(File.Exists(path));
            Assert.Empty(rig.Media.Deleted);
        }
    }

    public sealed class ScenarioARowPointingOutsideTheAdsRoot
    {
        [Fact]
        public async Task IsSkipped()
        {
            // Sad path: a stored path is untrusted; a safe-segment file must never be reached
            using var rig = new Rig();
            var path = rig.File("", "safe-segment.wav");
            rig.Media.Rows.Add(new AuthoredMediaFile(7, path));

            await rig.SweepAsync();

            Assert.True(File.Exists(path));
            Assert.Empty(rig.Media.Deleted);
        }
    }

    public sealed class ScenarioNoAdsLibrary
    {
        [Fact]
        public async Task NothingIsListed()
        {
            using var rig = new Rig(withAdsLibrary: false);

            await rig.SweepAsync();

            Assert.Null(rig.Media.ListedLibraryId);
        }
    }

    public sealed class ScenarioLeakedPreviewFiles
    {
        [Fact]
        public async Task AnUnstampedPreviewPastRetentionIsDeleted()
        {
            using var rig = new Rig();
            var leaked = rig.File("preview", "24-old.wav", ageDays: 8);

            await rig.SweepAsync();

            Assert.False(File.Exists(leaked));
        }

        [Fact]
        public async Task AFreshUnstampedPreviewIsKept()
        {
            // Sad path: a preview job may have just written it and not stamped it yet
            using var rig = new Rig();
            var fresh = rig.File("preview", "24-new.wav", ageDays: 1);

            await rig.SweepAsync();

            Assert.True(File.Exists(fresh));
        }

        [Fact]
        public async Task AStampedPreviewIsKept()
        {
            // Sad path: stamped previews belong to the lifecycle guardian's own sweep
            using var rig = new Rig();
            var stamped = rig.File("preview", "24-stamped.wav", ageDays: 8);
            rig.References.PreviewPaths.Add(stamped);

            await rig.SweepAsync();

            Assert.True(File.Exists(stamped));
        }
    }
}
