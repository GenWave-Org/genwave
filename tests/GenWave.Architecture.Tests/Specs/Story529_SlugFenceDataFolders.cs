// STORY-529 AC4 and AC7 (gh-#614 · SPEC F217 · PLAN T660) — the L12 fence over scratch trees.
// The rest of Story529 lives in GenWave.VoiceOff.Tests; the fence is internal to this assembly.

using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureTheSlugFenceAndTheDataFolders
{
    // AC4 — the slug fence allows the data folders
    public sealed class ScenarioTheSlugFenceAllowsTheDataFolders : IDisposable
    {
        readonly string root = Directory.CreateTempSubdirectory("story529-data-").FullName;
        readonly IReadOnlyList<LawViolation> violations;

        public ScenarioTheSlugFenceAllowsTheDataFolders()
        {
            foreach (var folder in new[] { "cards", "script" })
            {
                Directory.CreateDirectory(Path.Combine(root, folder));
                File.WriteAllText(Path.Combine(root, folder, "Planted.cs"), "// acme\n");
            }

            violations = VoiceOffSlugFence.Scan(root, ["acme"]);
        }

        public void Dispose() => Directory.Delete(root, true);

        [Fact]
        public void TheLawPasses() => Assert.Empty(violations);
    }

    // AC7 — a slug in code still fails the fence
    public sealed class ScenarioASlugInCodeStillFailsTheFence : IDisposable
    {
        readonly string root = Directory.CreateTempSubdirectory("story529-code-").FullName;
        readonly IReadOnlyList<LawViolation> violations;

        public ScenarioASlugInCodeStillFailsTheFence()
        {
            Directory.CreateDirectory(Path.Combine(root, "Render"));
            File.WriteAllText(Path.Combine(root, "Render", "Planted.cs"), "// acme\n");
            violations = VoiceOffSlugFence.Scan(root, ["acme"]);
        }

        public void Dispose() => Directory.Delete(root, true);

        [Fact]
        public void TheLawFails() => Assert.Single(violations);
    }
}
