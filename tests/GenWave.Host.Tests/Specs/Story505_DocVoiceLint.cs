// STORY-505 — Spec-citation prose can't come back (F215 · PLAN T628)
//
// BDD specification — xUnit. Drives the real tools/check-doc-voice.sh via ScriptProcess against
// scratch markdown files passed as arguments (file args override the script's built-in doc list),
// so no fact pins the prose of a real doc. AC5 reads ci.yml.

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureDocVoiceLint
{
    const string Script = "tools/check-doc-voice.sh";

    static readonly string RealPath = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin";

    static (int ExitCode, string StdOut, string StdErr) Lint(string markdown)
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "DOC.md");
        File.WriteAllText(file, markdown);
        return ScriptProcess.Run(Script, RealPath, args: file);
    }

    public static class ScenarioACitationInProseFails
    {
        [Fact(Skip = "pending: T628")]
        public static void Exits_non_zero_and_prints_file_line_token()
        {
            // Given a doc line "See SPEC F12 for details"
            // When  the lint runs over it
            // Then  it exits non-zero and prints file:line:SPEC F12
            var (exit, stdOut, stdErr) = Lint("# Title\n\nSee SPEC F12 for details.\n");

            Assert.NotEqual(0, exit);
            Assert.Contains("DOC.md:3:SPEC F12", stdOut + stdErr, StringComparison.Ordinal);
        }
    }

    public static class ScenarioEveryForbiddenTokenIsCaught
    {
        [Theory(Skip = "pending: T628")]
        [InlineData("Shipped in STORY-12.")]
        [InlineData("Tracked in gh-#34.")]
        [InlineData("Done in T123 last week.")]
        [InlineData("Available since v5.2.")]
        [InlineData("As of v4 this is the default.")]
        public static void Each_token_is_reported(string line)
        {
            var (exit, stdOut, stdErr) = Lint($"Intro.\n{line}\n");

            Assert.NotEqual(0, exit);
            Assert.Contains("DOC.md:2:", stdOut + stdErr, StringComparison.Ordinal);
        }
    }

    public static class ScenarioCodeIsExempt
    {
        [Fact(Skip = "pending: T628")]
        public static void Tokens_in_fences_and_backtick_spans_pass()
        {
            var markdown =
                "Run `gh-#398` and `T123` checks.\n\n" +
                "```bash\n# SPEC F12, STORY-12, since v5.2\n```\n";

            var (exit, stdOut, stdErr) = Lint(markdown);

            Assert.True(exit == 0, $"exit {exit}\n{stdOut}\n{stdErr}");
        }
    }

    public static class ScenarioLookalikesPass
    {
        [Fact(Skip = "pending: T628")]
        public static void T1000_AT123B_and_v5_pass()
        {
            var (exit, stdOut, stdErr) = Lint("The T1000 is not AT123B, and the v5 stack is fine.\n");

            Assert.True(exit == 0, $"exit {exit}\n{stdOut}\n{stdErr}");
        }
    }

    public static class ScenarioCiRunsIt
    {
        [Fact(Skip = "pending: T628")]
        public static void Build_test_runs_the_lint_after_the_seam_check()
        {
            var ci = File.ReadAllText(Path.Combine(
                RepoRootLocator.Find(AppContext.BaseDirectory), ".github", "workflows", "ci.yml"));

            var seam = ci.IndexOf("SEAMS.md drift check", StringComparison.Ordinal);
            var lint = ci.IndexOf("tools/check-doc-voice.sh", StringComparison.Ordinal);

            Assert.True(seam >= 0, "seam check step missing");
            Assert.True(lint > seam, "doc-voice lint must run after the seam check");
        }
    }
}
