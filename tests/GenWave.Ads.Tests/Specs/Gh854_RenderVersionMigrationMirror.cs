// gh-#854 — db/48's own ADD COLUMN text is mirrored, identically, in db/06's fresh-init CREATE TABLE
// (the Story435_FailedJobKind.cs ScenarioTheFailedKindColumnIsMirroredInFreshInit precedent, one
// project over). No DB needed — a plain text-equality pin against both files on disk.

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Support;

public static class Gh854FeatureRenderVersionIsMirroredInFreshInit
{
    const string RenderVersionColumnText = "render_version int not null default 0";

    public sealed class ScenarioTheRenderVersionColumnIsMirroredInFreshInit
    {
        readonly string db48;
        readonly string db06;

        public ScenarioTheRenderVersionColumnIsMirroredInFreshInit()
        {
            var repoRoot = RepoRootLocator.Find(AppContext.BaseDirectory);
            db48 = File.ReadAllText(Path.Combine(repoRoot, "db", "48-ad-spot-render-version-migration.sh"));
            db06 = File.ReadAllText(Path.Combine(repoRoot, "db", "06-station-settings-migration.sh"));
        }

        [Fact]
        public void Db48CarriesTheColumnDefinition()
            => Assert.Contains(RenderVersionColumnText, db48);

        [Fact]
        public void Db06CarriesTheIdenticalColumnDefinition()
            // The fresh-init mirror must define the SAME column as db/48's own ALTER — a fresh
            // install and an upgraded box must carry render_version identically.
            => Assert.Contains(RenderVersionColumnText, db06);
    }
}

public static class Gh854FeaturePendingRetireMediaIdIsMirroredInFreshInit
{
    const string PendingRetireMediaIdColumnText = "pending_retire_media_id bigint null";

    public sealed class ScenarioThePendingRetireMediaIdColumnIsMirroredInFreshInit
    {
        readonly string db48;
        readonly string db06;

        public ScenarioThePendingRetireMediaIdColumnIsMirroredInFreshInit()
        {
            var repoRoot = RepoRootLocator.Find(AppContext.BaseDirectory);
            db48 = File.ReadAllText(Path.Combine(repoRoot, "db", "48-ad-spot-render-version-migration.sh"));
            db06 = File.ReadAllText(Path.Combine(repoRoot, "db", "06-station-settings-migration.sh"));
        }

        [Fact]
        public void Db48CarriesTheColumnDefinition()
            => Assert.Contains(PendingRetireMediaIdColumnText, db48);

        [Fact]
        public void Db06CarriesTheIdenticalColumnDefinition()
            // The fresh-init mirror must define the SAME column as db/48's own second ALTER — a
            // fresh install and an upgraded box must carry pending_retire_media_id identically.
            => Assert.Contains(PendingRetireMediaIdColumnText, db06);
    }
}

public static class Gh854FeaturePendingConfirmMediaIdIsMirroredInFreshInit
{
    const string PendingConfirmMediaIdColumnText = "pending_confirm_media_id bigint null";

    public sealed class ScenarioThePendingConfirmMediaIdColumnIsMirroredInFreshInit
    {
        readonly string db48;
        readonly string db06;

        public ScenarioThePendingConfirmMediaIdColumnIsMirroredInFreshInit()
        {
            var repoRoot = RepoRootLocator.Find(AppContext.BaseDirectory);
            db48 = File.ReadAllText(Path.Combine(repoRoot, "db", "48-ad-spot-render-version-migration.sh"));
            db06 = File.ReadAllText(Path.Combine(repoRoot, "db", "06-station-settings-migration.sh"));
        }

        [Fact]
        public void Db48CarriesTheColumnDefinition()
            => Assert.Contains(PendingConfirmMediaIdColumnText, db48);

        [Fact]
        public void Db06CarriesTheIdenticalColumnDefinition()
            // The fresh-init mirror must define the SAME column as db/48's own third ALTER — a
            // fresh install and an upgraded box must carry pending_confirm_media_id identically.
            => Assert.Contains(PendingConfirmMediaIdColumnText, db06);
    }
}
