// STORY-379 — The gardener may fix my files, when I say so (SPEC F154 · PLAN T379)
//
// BDD specification — xUnit. Pure planner + jail: no Postgres, no ASP.NET Core host (the Host-level
// dry-run/confirm wire is T381's own Story379_TheGardenerMayFixMyFiles.cs, left pending). Real temp
// directories/files/symlinks throughout — never a mocked filesystem for the jail, since the jail's
// own canonicalise/symlink-resolve/root-prefix logic is exactly the thing under test; the ONE double
// in this file, RecordingFileSystemProbe, wraps the REAL FileSystemProbe and only observes whether
// Kind/ResolveLinks were called, it never fakes an answer. Every filesystem-backed Scenario class
// implements IDisposable: its constructor is xUnit's own per-fact Arrange (a fresh instance per
// [Fact]), Dispose is the matching per-fact cleanup.
//
// T379 review pass (2026-08-30): B1-B6 blocking findings and the N1-N9b non-blocking ones ruled in
// are folded in here alongside the facts that already existed; each is called out by its own finding
// id in the section banner it lives under.

using System.Reflection;
using System.Text;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Garden.FileActions;
using GenWave.MediaLibrary.Options;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureFileActionPlannerAndJail
{
    static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------------
    // Shared test scaffolding
    // ---------------------------------------------------------------------

    // T381 review N4: fileTagReader defaults to a fresh RecordingFileTagReader answering null (a
    // tagless read) — every fact that is NOT specifically about the retag diff itself passes no
    // override and never inspects it; ScenarioRetagDiff's own facts (and the "zero reads on a
    // refused retag" pin below) build their OWN instance instead, so they can both script the
    // answer AND assert on ReadCount afterward.
    static FileActionPlanner BuildPlanner(
        string root, string[]? exemptRoots = null, IFileSystemProbe? probe = null, IFileTagReader? fileTagReader = null) =>
        new(
            new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions { MediaRoot = root }),
            new FakeOptionsMonitor<ScanOptions>(new ScanOptions { QuarantineExemptRoots = exemptRoots ?? [] }),
            probe ?? new FileSystemProbe(),
            fileTagReader ?? new RecordingFileTagReader());

    /// <summary>A root directory containing a real subject file at <c>{root}/a/{fileName}</c> — the
    /// common arrangement every jail fact starts from.</summary>
    static (string Root, string SubjectDir, string SubjectPath) CreateSubjectTree(string fileName = "x.mp3")
    {
        var root = TestMedia.NewTempDir();
        var subjectDir = Path.Combine(root, "a");
        Directory.CreateDirectory(subjectDir);
        var subjectPath = Path.Combine(subjectDir, fileName);
        File.WriteAllBytes(subjectPath, [1, 2, 3]);
        return (root, subjectDir, subjectPath);
    }

    static FileActionSubject Subject(
        string path,
        long mediaId = 42,
        string xmin = "100",
        long libraryId = 1,
        string? artist = null,
        string? title = null,
        string? album = null,
        int? year = null,
        string? genre = null) =>
        new(mediaId, xmin, path, libraryId, artist, title, album, year, genre);

    static byte[] TestKey() => Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef");

    static byte[] OtherTestKey() => Encoding.UTF8.GetBytes("fedcba9876543210fedcba9876543210");

    // ---------------------------------------------------------------------
    // AC2 — dry-run returns a plan and a token
    // ---------------------------------------------------------------------

    public sealed class ScenarioRenamePlan : IDisposable
    {
        readonly string root;
        readonly string subjectDir;
        readonly string subjectPath;
        readonly FileActionPlanner planner;

        public ScenarioRenamePlan()
        {
            (root, subjectDir, subjectPath) = CreateSubjectTree();
            planner = BuildPlanner(root);
        }

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void FromIsTheSubjectPath()
        {
            var result = planner.Plan(Subject(subjectPath, artist: "Artist", title: "Title"), FileActionVerb.Rename, null, Now);

            Assert.Equal(subjectPath, result.Plan!.From);
        }

        [Fact]
        public void ToIsTheTemplateName()
        {
            var result = planner.Plan(Subject(subjectPath, artist: "Artist", title: "Title"), FileActionVerb.Rename, null, Now);

            Assert.Equal(Path.Combine(subjectDir, "Artist - Title.mp3"), result.Plan!.To);
        }

        [Fact]
        public void TheMintedTokenRoundTripsToAnEqualPlan()
        {
            var result = planner.Plan(Subject(subjectPath, artist: "Artist", title: "Title"), FileActionVerb.Rename, null, Now);
            var tokens = new HmacFileActionPlanTokens(TestKey());
            var token = tokens.Mint(result.Plan!, Now);

            tokens.TryRead(token, Now, out var readPlan, out _);

            Assert.Equal(result.Plan, readPlan);
        }

        [Fact]
        public void AnOperatorSuppliedNameIsUsedVerbatim()
        {
            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Rename, "custom-name.mp3", Now);

            Assert.Equal(Path.Combine(subjectDir, "custom-name.mp3"), result.Plan!.To);
        }
    }

    // ---------------------------------------------------------------------
    // Retag diff (SPEC F154.1, F154.5)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRetagDiff : IDisposable
    {
        readonly string root;
        readonly string subjectPath;

        public ScenarioRetagDiff()
        {
            (root, _, subjectPath) = CreateSubjectTree();
        }

        public void Dispose() => Directory.Delete(root, recursive: true);

        // T381 review N4: the file's own tags now arrive via IFileTagReader, read by the planner
        // itself — each fact below scripts its OWN RecordingFileTagReader answer rather than
        // passing tags on the subject.
        [Fact]
        public void ADifferingFieldProducesOneTagChange()
        {
            var planner = BuildPlanner(root, fileTagReader: new RecordingFileTagReader(new FileTags("B", null, null, null, null)));
            var subject = Subject(subjectPath, artist: "A");

            var result = planner.Plan(subject, FileActionVerb.Retag, null, Now);

            Assert.Equal(new TagChange("artist", "B", "A"), Assert.Single(result.Plan!.TagDiff));
        }

        [Fact]
        public void EqualTagsAreRefusedAsNothingToRetag()
        {
            var planner = BuildPlanner(root, fileTagReader: new RecordingFileTagReader(new FileTags("A", null, null, null, null)));
            var subject = Subject(subjectPath, artist: "A");

            var result = planner.Plan(subject, FileActionVerb.Retag, null, Now);

            Assert.Equal(FileActionRule.NothingToRetag, result.Refusal!.Value.Rule);
        }

        [Fact]
        public void ANullCatalogFieldProducesNoChangeForIt()
        {
            var planner = BuildPlanner(root, fileTagReader: new RecordingFileTagReader(new FileTags("B", null, "SomeFileAlbum", null, null)));
            var subject = Subject(subjectPath, artist: "A", album: null);

            var result = planner.Plan(subject, FileActionVerb.Retag, null, Now);

            Assert.DoesNotContain(result.Plan!.TagDiff, change => change.Field == "album");
        }

        // T379 review N8 — a whitespace-only catalog value is "no opinion" too, same as null.
        [Fact]
        public void AWhitespaceOnlyCatalogFieldProducesNoChangeForIt()
        {
            var planner = BuildPlanner(root, fileTagReader: new RecordingFileTagReader(new FileTags("SomeFileArtist", null, null, null, null)));
            var subject = Subject(subjectPath, artist: "   ", title: "T");

            var result = planner.Plan(subject, FileActionVerb.Retag, null, Now);

            Assert.DoesNotContain(result.Plan!.TagDiff, change => change.Field == "artist");
        }
    }

    // ---------------------------------------------------------------------
    // AC9 — traversal is refused, before any filesystem probe call (T379 review N7)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTraversalInATargetIsRefused : IDisposable
    {
        readonly string root;
        readonly RecordingFileSystemProbe probe;
        readonly FileActionPlanResult result;

        public ScenarioTraversalInATargetIsRefused()
        {
            (root, _, var subjectPath) = CreateSubjectTree();
            probe = new RecordingFileSystemProbe(new FileSystemProbe());
            var planner = BuildPlanner(root, probe: probe);

            result = planner.Plan(Subject(subjectPath), FileActionVerb.Move, "../../etc/x.mp3", Now);
        }

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void TheRuleIsTraversal() =>
            Assert.Equal(FileActionRule.Traversal, result.Refusal!.Value.Rule);

        [Fact]
        public void KindWasNeverCalled() =>
            Assert.False(probe.KindWasCalled);

        [Fact]
        public void ResolveLinksWasNeverCalled() =>
            Assert.False(probe.ResolveLinksWasCalled);
    }

    public sealed class ScenarioARefusalNeverCarriesAPath
    {
        // AC9's own "path is not echoed" pin, pinned mechanically rather than per-refusal: no
        // FileActionRefusal, today or added later, can carry a string at all.
        [Fact]
        public void TheRefusalTypeExposesNoStringMember()
        {
            var stringProperties = typeof(FileActionRefusal)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.PropertyType == typeof(string));

            Assert.Empty(stringProperties);
        }
    }

    // ---------------------------------------------------------------------
    // AC10 — a symlink escape is refused, before Kind is consulted
    // ---------------------------------------------------------------------

    public sealed class ScenarioSymlinkEscapeIsRefusedBeforeExists : IDisposable
    {
        readonly string root;
        readonly string outsideDir;
        readonly RecordingFileSystemProbe probe;
        readonly FileActionPlanResult result;

        public ScenarioSymlinkEscapeIsRefusedBeforeExists()
        {
            (root, _, var subjectPath) = CreateSubjectTree();
            outsideDir = TestMedia.NewTempDir();
            var linkDir = Path.Combine(root, "link");
            Directory.CreateSymbolicLink(linkDir, outsideDir);

            probe = new RecordingFileSystemProbe(new FileSystemProbe());
            var planner = BuildPlanner(root, probe: probe);

            result = planner.Plan(Subject(subjectPath), FileActionVerb.Move, linkDir, Now);
        }

        public void Dispose()
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outsideDir, recursive: true);
        }

        [Fact]
        public void TheRuleIsSymlinkEscape() =>
            Assert.Equal(FileActionRule.SymlinkEscape, result.Refusal!.Value.Rule);

        // ResolveLinks IS expected here — resolving the link is how the escape is even detected.
        // Only Kind (the never-overwrite existence probe) must stay silent before a refusal.
        [Fact]
        public void KindWasNeverCalled() =>
            Assert.False(probe.KindWasCalled);
    }

    // ---------------------------------------------------------------------
    // AC11 — another root, or an exempt root, is refused
    // ---------------------------------------------------------------------

    public sealed class ScenarioATargetUnderAnotherRootIsRefused : IDisposable
    {
        readonly string root;
        readonly string otherRoot;
        readonly string subjectPath;

        public ScenarioATargetUnderAnotherRootIsRefused()
        {
            (root, _, subjectPath) = CreateSubjectTree();
            otherRoot = TestMedia.NewTempDir();
        }

        public void Dispose()
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(otherRoot, recursive: true);
        }

        [Fact]
        public void TheRuleIsOutsideRoot()
        {
            var planner = BuildPlanner(root);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Move, otherRoot, Now);

            Assert.Equal(FileActionRule.OutsideRoot, result.Refusal!.Value.Rule);
        }
    }

    public sealed class ScenarioATargetUnderAnExemptRootIsRefused : IDisposable
    {
        readonly string root;
        readonly string exemptRoot;
        readonly string subjectPath;

        public ScenarioATargetUnderAnExemptRootIsRefused()
        {
            (root, _, subjectPath) = CreateSubjectTree();
            exemptRoot = TestMedia.NewTempDir();
        }

        public void Dispose()
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(exemptRoot, recursive: true);
        }

        [Fact]
        public void TheRuleIsExemptRoot()
        {
            var planner = BuildPlanner(root, [exemptRoot]);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Move, exemptRoot, Now);

            Assert.Equal(FileActionRule.ExemptRoot, result.Refusal!.Value.Rule);
        }
    }

    // ---------------------------------------------------------------------
    // T379 review B2/B3 — the ONE destination gate applies to retag too, and sees resolved paths
    // ---------------------------------------------------------------------

    public sealed class ScenarioARetagSubjectUnderAnExemptRootIsRefused : IDisposable
    {
        readonly string exemptDir;
        readonly string root;
        readonly string subjectPath;

        public ScenarioARetagSubjectUnderAnExemptRootIsRefused()
        {
            root = TestMedia.NewTempDir();
            exemptDir = TestMedia.NewTempDir();
            subjectPath = Path.Combine(exemptDir, "x.mp3");
            File.WriteAllBytes(subjectPath, [1]);
        }

        public void Dispose()
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(exemptDir, recursive: true);
        }

        // B2: a retag never moves the file — its own destination IS its source — so the subject's
        // own exempt-root check must still refuse it, not just a computed rename/move target's.
        [Fact]
        public void TheRuleIsExemptRoot()
        {
            var planner = BuildPlanner(root, [exemptDir]);

            var result = planner.Plan(Subject(subjectPath, artist: "A"), FileActionVerb.Retag, null, Now);

            Assert.Equal(FileActionRule.ExemptRoot, result.Refusal!.Value.Rule);
        }

        // T381 review N4: the file's own tags must never be opened for a subject the destination
        // gate has already refused — RecordingFileTagReader.ReadCount is the direct proof PlanRetag
        // was never reached at all, not merely that its RESULT happened to discard whatever it read.
        [Fact]
        public void TheFileTagReaderIsNeverCalled()
        {
            var reader = new RecordingFileTagReader();
            var planner = BuildPlanner(root, [exemptDir], fileTagReader: reader);

            var result = planner.Plan(Subject(subjectPath, artist: "A"), FileActionVerb.Retag, null, Now);

            Assert.True(result.IsRefused);
            Assert.Equal(0, reader.ReadCount);
        }
    }

    public sealed class ScenarioASymlinkInsideTheRootPointingAtAnExemptDirIsRefused : IDisposable
    {
        readonly string root;
        readonly string subjectPath;
        readonly string sneakLink;

        public ScenarioASymlinkInsideTheRootPointingAtAnExemptDirIsRefused()
        {
            root = TestMedia.NewTempDir();
            var subjectDir = Path.Combine(root, "a");
            Directory.CreateDirectory(subjectDir);
            subjectPath = Path.Combine(subjectDir, "x.mp3");
            File.WriteAllBytes(subjectPath, [1]);

            var authoredDir = Path.Combine(root, "authored");
            Directory.CreateDirectory(authoredDir);
            sneakLink = Path.Combine(root, "sneak");
            Directory.CreateSymbolicLink(sneakLink, authoredDir);

            ExemptDir = authoredDir;
        }

        string ExemptDir { get; }

        public void Dispose() => Directory.Delete(root, recursive: true);

        // B3: canonically "sneak" is just an ordinary directory INSIDE the root — only its
        // link-resolved form reveals it actually lands in the exempt "authored" directory. The
        // exempt check must see both forms, not just the canonical one.
        [Fact]
        public void TheRuleIsExemptRoot()
        {
            var planner = BuildPlanner(root, [ExemptDir]);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Move, sneakLink, Now);

            Assert.Equal(FileActionRule.ExemptRoot, result.Refusal!.Value.Rule);
        }
    }

    // ---------------------------------------------------------------------
    // T379 review B1 — the plan (and the existence probe) carry the CANONICAL target, never the raw
    // one; a non-rooted move target is refused before the process's own working directory can
    // participate at all.
    // ---------------------------------------------------------------------

    public sealed class ScenarioADotSegmentInATargetPlansTheCanonicalPath : IDisposable
    {
        readonly string root;
        readonly string subjectPath;
        readonly string targetDir;

        public ScenarioADotSegmentInATargetPlansTheCanonicalPath()
        {
            (root, _, subjectPath) = CreateSubjectTree();
            targetDir = Path.Combine(root, "b");
            Directory.CreateDirectory(targetDir);
        }

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void ToIsTheCanonicalPathWithNoDotSegment()
        {
            var planner = BuildPlanner(root);
            var rawTarget = Path.Combine(root, ".", "b");

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Move, rawTarget, Now);

            Assert.Equal(Path.GetFullPath(Path.Combine(targetDir, "x.mp3")), result.Plan!.To);
        }
    }

    public sealed class ScenarioARelativeMoveTargetIsRefused : IDisposable
    {
        readonly string root;
        readonly string subjectPath;

        public ScenarioARelativeMoveTargetIsRefused() => (root, _, subjectPath) = CreateSubjectTree();

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void TheRuleIsOutsideRoot()
        {
            var planner = BuildPlanner(root);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Move, "b", Now);

            Assert.Equal(FileActionRule.OutsideRoot, result.Refusal!.Value.Rule);
        }
    }

    // T379 review round 2 item 4 — a null or empty move target is refused, never thrown; the
    // planner must never throw on any input shape the endpoint could pass.
    public sealed class ScenarioAMissingMoveTargetIsRefusedWithoutThrowing : IDisposable
    {
        readonly string root;
        readonly string subjectPath;

        public ScenarioAMissingMoveTargetIsRefusedWithoutThrowing() => (root, _, subjectPath) = CreateSubjectTree();

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void ANullTargetIsMissingTarget()
        {
            var planner = BuildPlanner(root);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Move, null, Now);

            Assert.Equal(FileActionRule.MissingTarget, result.Refusal!.Value.Rule);
        }

        [Fact]
        public void AnEmptyTargetIsMissingTarget()
        {
            var planner = BuildPlanner(root);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Move, "", Now);

            Assert.Equal(FileActionRule.MissingTarget, result.Refusal!.Value.Rule);
        }
    }

    // ---------------------------------------------------------------------
    // AC12 — never overwrite; T379 review N9b — a move directory that is a FILE is refused too
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnExistingTargetIsRefused : IDisposable
    {
        readonly string root;
        readonly string subjectPath;
        readonly RecordingFileSystemProbe probe;
        readonly FileActionPlanResult result;

        public ScenarioAnExistingTargetIsRefused()
        {
            string subjectDir;
            (root, subjectDir, subjectPath) = CreateSubjectTree();
            File.WriteAllBytes(Path.Combine(subjectDir, "already-there.mp3"), [9]);

            probe = new RecordingFileSystemProbe(new FileSystemProbe());
            var planner = BuildPlanner(root, probe: probe);

            result = planner.Plan(Subject(subjectPath), FileActionVerb.Rename, "already-there.mp3", Now);
        }

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void TheRuleIsTargetExists() =>
            Assert.Equal(FileActionRule.TargetExists, result.Refusal!.Value.Rule);

        [Fact]
        public void KindWasConsultedOnlyOnceTheJailAlreadyPassed() =>
            Assert.True(probe.KindWasCalled);
    }

    public sealed class ScenarioAMoveTargetThatIsAFileIsRefused : IDisposable
    {
        readonly string root;
        readonly string subjectPath;
        readonly string targetFile;

        public ScenarioAMoveTargetThatIsAFileIsRefused()
        {
            (root, _, subjectPath) = CreateSubjectTree();
            targetFile = Path.Combine(root, "not-a-directory.mp3");
            File.WriteAllBytes(targetFile, [1]);
        }

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void TheRuleIsTargetNotADirectory()
        {
            var planner = BuildPlanner(root);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Move, targetFile, Now);

            Assert.Equal(FileActionRule.TargetNotADirectory, result.Refusal!.Value.Rule);
        }
    }

    // T379 review round 2 item 3 — the planner never implies an mkdir: a move directory that does
    // not exist YET is refused exactly like one occupied by a file, never silently accepted.
    public sealed class ScenarioAMoveTargetThatDoesNotExistYetIsRefused : IDisposable
    {
        readonly string root;
        readonly string subjectPath;
        readonly string notYetCreatedDir;

        public ScenarioAMoveTargetThatDoesNotExistYetIsRefused()
        {
            (root, _, subjectPath) = CreateSubjectTree();
            notYetCreatedDir = Path.Combine(root, "does-not-exist-yet");
        }

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void TheRuleIsTargetNotADirectory()
        {
            var planner = BuildPlanner(root);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Move, notYetCreatedDir, Now);

            Assert.Equal(FileActionRule.TargetNotADirectory, result.Refusal!.Value.Rule);
        }
    }

    // ---------------------------------------------------------------------
    // AC14 — plan token expiry, tampering, and key mismatch; T379 review N1/N3/N4
    // ---------------------------------------------------------------------

    public sealed class ScenarioTokenExpiry
    {
        static readonly FileActionPlan Plan = new(
            42, "100", FileActionVerb.Rename, "/media/a/x.mp3", "/media/a/y.mp3", [], Now + TimeSpan.FromMinutes(10));

        [Fact]
        public void AtExpiresAtExactlyTheTokenIsExpired()
        {
            var tokens = new HmacFileActionPlanTokens(TestKey());
            var token = tokens.Mint(Plan, Now);

            tokens.TryRead(token, Plan.ExpiresAt, out _, out var failure);

            Assert.Equal(PlanTokenFailure.Expired, failure);
        }

        [Fact]
        public void OneTickBeforeExpiresAtTheTokenStillReadsBackTheSamePlan()
        {
            var tokens = new HmacFileActionPlanTokens(TestKey());
            var token = tokens.Mint(Plan, Now);

            tokens.TryRead(token, Plan.ExpiresAt - TimeSpan.FromTicks(1), out var readPlan, out _);

            Assert.Equal(Plan, readPlan);
        }

        [Fact]
        public void ATamperedTokenIsInvalid()
        {
            var tokens = new HmacFileActionPlanTokens(TestKey());
            var token = tokens.Mint(Plan, Now);
            var tampered = FlipOneChar(token);

            tokens.TryRead(tampered, Now, out _, out var failure);

            Assert.Equal(PlanTokenFailure.Invalid, failure);
        }

        [Fact]
        public void ATokenMintedWithADifferentKeyIsInvalid()
        {
            var mintTokens = new HmacFileActionPlanTokens(TestKey());
            var readTokens = new HmacFileActionPlanTokens(OtherTestKey());
            var token = mintTokens.Mint(Plan, Now);

            readTokens.TryRead(token, Now, out _, out var failure);

            Assert.Equal(PlanTokenFailure.Invalid, failure);
        }

        // T379 review N3 — a null token is a caller bug, not a crash.
        [Fact]
        public void ANullTokenIsInvalidNotAThrow()
        {
            var tokens = new HmacFileActionPlanTokens(TestKey());

            tokens.TryRead(null!, Now, out _, out var failure);

            Assert.Equal(PlanTokenFailure.Invalid, failure);
        }

        static string FlipOneChar(string token)
        {
            var payloadLength = token.IndexOf('.');
            var index = payloadLength / 2;
            var chars = token.ToCharArray();
            chars[index] = chars[index] == 'A' ? 'B' : 'A';
            return new string(chars);
        }
    }

    // T379 review N1 — every field is base64url-encoded before being pipe-joined, so a path
    // containing a literal '|' still round-trips instead of desynchronising the split.
    public sealed class ScenarioAPipeInAPathRoundTrips : IDisposable
    {
        readonly string root;
        readonly string subjectPath;

        public ScenarioAPipeInAPathRoundTrips() => (root, _, subjectPath) = CreateSubjectTree();

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void TheMintedTokenRoundTripsToAnEqualPlan()
        {
            var planner = BuildPlanner(root);
            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Rename, "a|b.mp3", Now);
            var tokens = new HmacFileActionPlanTokens(TestKey());

            var token = tokens.Mint(result.Plan!, Now);
            tokens.TryRead(token, Now, out var readPlan, out _);

            Assert.Equal(result.Plan, readPlan);
        }
    }

    // ---------------------------------------------------------------------
    // T379 review N5 — the HMAC key is copied and length-guarded
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheHmacKeyIsGuardedAndCopied
    {
        [Fact]
        public void AKeyShorterThanThirtyTwoBytesThrows() =>
            Assert.Throws<ArgumentException>(() => new HmacFileActionPlanTokens(new byte[31]));

        [Fact]
        public void MutatingTheCallersArrayAfterConstructionDoesNotAffectSigning()
        {
            var key = TestKey();
            var tokens = new HmacFileActionPlanTokens(key);
            var plan = new FileActionPlan(
                42, "100", FileActionVerb.Rename, "/media/a/x.mp3", "/media/a/y.mp3", [], Now + TimeSpan.FromMinutes(10));
            var token = tokens.Mint(plan, Now);

            Array.Clear(key);

            tokens.TryRead(token, Now, out var readPlan, out _);
            Assert.Equal(plan, readPlan);
        }
    }

    // ---------------------------------------------------------------------
    // AC7 — the confirm-step binding check
    // ---------------------------------------------------------------------

    public sealed class ScenarioPlanBindingMatches
    {
        static readonly FileActionPlan Plan = new(
            42, "100", FileActionVerb.Rename, "/media/a/x.mp3", "/media/a/y.mp3", [], Now + TimeSpan.FromMinutes(10));

        [Fact]
        public void FalseWhenXminHasChanged() =>
            Assert.False(PlanBinding.Matches(Plan, "999", "/media/a/x.mp3"));

        [Fact]
        public void FalseWhenTheCurrentPathHasMoved() =>
            Assert.False(PlanBinding.Matches(Plan, "100", "/media/a/moved.mp3"));

        [Fact]
        public void TrueWhenBothStillMatch() =>
            Assert.True(PlanBinding.Matches(Plan, "100", "/media/a/x.mp3"));
    }

    // ---------------------------------------------------------------------
    // Mutants — subject-side and library-scope refusals
    // ---------------------------------------------------------------------

    public sealed class ScenarioASubjectOutsideTheRootIsRefused : IDisposable
    {
        readonly string root;
        readonly string outsideDir;
        readonly string subjectPath;

        public ScenarioASubjectOutsideTheRootIsRefused()
        {
            root = TestMedia.NewTempDir();
            outsideDir = TestMedia.NewTempDir();
            subjectPath = Path.Combine(outsideDir, "x.mp3");
            File.WriteAllBytes(subjectPath, [1]);
        }

        public void Dispose()
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outsideDir, recursive: true);
        }

        [Fact]
        public void TheRuleIsSubjectOutsideRoot()
        {
            var planner = BuildPlanner(root);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Retag, null, Now);

            Assert.Equal(FileActionRule.SubjectOutsideRoot, result.Refusal!.Value.Rule);
        }
    }

    public sealed class ScenarioALibraryIdOtherThanTheScannedOneIsRefused : IDisposable
    {
        readonly string root;
        readonly string subjectPath;

        public ScenarioALibraryIdOtherThanTheScannedOneIsRefused() => (root, _, subjectPath) = CreateSubjectTree();

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void TheRuleIsNotScannedLibrary()
        {
            var planner = BuildPlanner(root);

            var result = planner.Plan(Subject(subjectPath, libraryId: 2), FileActionVerb.Retag, null, Now);

            Assert.Equal(FileActionRule.NotScannedLibrary, result.Refusal!.Value.Rule);
        }
    }

    // T379 review B4 — a subject with no directory component (MediaRoot itself) refuses cleanly,
    // it never throws.
    public sealed class ScenarioTheRootItselfAsASubjectRefusesWithoutThrowing : IDisposable
    {
        readonly string root;

        public ScenarioTheRootItselfAsASubjectRefusesWithoutThrowing() => root = TestMedia.NewTempDir();

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void TheRootAsItsOwnSubjectIsRefusedAsSubjectOutsideRoot()
        {
            // MediaRoot = "/" reproduces Path.GetDirectoryName returning null for the subject's own
            // path — the narrowest real repro is the filesystem root itself; a media root that is
            // ever configured to "/" is the same shape.
            var planner = BuildPlanner("/");

            var result = planner.Plan(Subject("/"), FileActionVerb.Rename, null, Now);

            Assert.Equal(FileActionRule.SubjectOutsideRoot, result.Refusal!.Value.Rule);
        }
    }

    // T379 review round 2 item 4 — Path.GetFullPath("") throws; an empty subject path is refused
    // before that call is ever reached, never left to throw.
    public sealed class ScenarioAnEmptySubjectPathRefusesWithoutThrowing : IDisposable
    {
        readonly string root;

        public ScenarioAnEmptySubjectPathRefusesWithoutThrowing() => root = TestMedia.NewTempDir();

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void AnEmptySubjectPathIsRefusedAsSubjectOutsideRoot()
        {
            var planner = BuildPlanner(root);

            var result = planner.Plan(Subject(""), FileActionVerb.Retag, null, Now);

            Assert.Equal(FileActionRule.SubjectOutsideRoot, result.Refusal!.Value.Rule);
        }
    }

    // ---------------------------------------------------------------------
    // Mutants — target-side refusals
    // ---------------------------------------------------------------------

    public sealed class ScenarioARenameToAParentDirectoryIsRefusedAsTraversal : IDisposable
    {
        readonly string root;
        readonly string subjectPath;

        public ScenarioARenameToAParentDirectoryIsRefusedAsTraversal() => (root, _, subjectPath) = CreateSubjectTree();

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void TheRuleIsTraversal()
        {
            var planner = BuildPlanner(root);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Rename, "../x.mp3", Now);

            Assert.Equal(FileActionRule.Traversal, result.Refusal!.Value.Rule);
        }
    }

    public sealed class ScenarioAMoveToTheSameDirectoryIsRefused : IDisposable
    {
        readonly string root;
        readonly string subjectDir;
        readonly string subjectPath;

        public ScenarioAMoveToTheSameDirectoryIsRefused() => (root, subjectDir, subjectPath) = CreateSubjectTree();

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void TheRuleIsSameAsSource()
        {
            var planner = BuildPlanner(root);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Move, subjectDir, Now);

            Assert.Equal(FileActionRule.SameAsSource, result.Refusal!.Value.Rule);
        }
    }

    // T379 review B5 — control characters in an operator-supplied name
    public sealed class ScenarioAControlCharacterInAnOperatorNameIsRefused : IDisposable
    {
        readonly string root;
        readonly string subjectPath;

        public ScenarioAControlCharacterInAnOperatorNameIsRefused() => (root, _, subjectPath) = CreateSubjectTree();

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void ALineFeedIsInvalidName()
        {
            var planner = BuildPlanner(root);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Rename, "a\nb.mp3", Now);

            Assert.Equal(FileActionRule.InvalidName, result.Refusal!.Value.Rule);
        }

        [Fact]
        public void AnEscapeCharacterIsInvalidName()
        {
            var planner = BuildPlanner(root);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Rename, "a\u001Bb.mp3", Now);

            Assert.Equal(FileActionRule.InvalidName, result.Refusal!.Value.Rule);
        }
    }

    // T379 review round 2 item 1 — a leading dot is Hidden on this deploy target and would vanish
    // from the very next scan tick (EnumerationOptions' own default AttributesToSkip).
    public sealed class ScenarioALeadingDotInAnOperatorNameIsRefused : IDisposable
    {
        readonly string root;
        readonly string subjectPath;

        public ScenarioALeadingDotInAnOperatorNameIsRefused() => (root, _, subjectPath) = CreateSubjectTree();

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void ADottedNameWithARealBaseIsInvalidName()
        {
            var planner = BuildPlanner(root);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Rename, ".hidden.mp3", Now);

            Assert.Equal(FileActionRule.InvalidName, result.Refusal!.Value.Rule);
        }

        [Fact]
        public void ABareDottedExtensionIsInvalidName()
        {
            var planner = BuildPlanner(root);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Rename, ".mp3", Now);

            Assert.Equal(FileActionRule.InvalidName, result.Refusal!.Value.Rule);
        }
    }

    // T379 review N9a — the extension is the container's, not the operator's to rename away.
    public sealed class ScenarioARenameWithADifferentExtensionIsRefused : IDisposable
    {
        readonly string root;
        readonly string subjectPath;

        public ScenarioARenameWithADifferentExtensionIsRefused() => (root, _, subjectPath) = CreateSubjectTree();

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void TheRuleIsInvalidName()
        {
            var planner = BuildPlanner(root);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Rename, "renamed.flac", Now);

            Assert.Equal(FileActionRule.InvalidName, result.Refusal!.Value.Rule);
        }
    }

    // T379 review B6 — the template's own generated name is validated (and truncated if needed) too.
    public sealed class ScenarioALongArtistYieldsATemplateNameWithinTheByteLimit : IDisposable
    {
        readonly string root;
        readonly string subjectPath;

        public ScenarioALongArtistYieldsATemplateNameWithinTheByteLimit() => (root, _, subjectPath) = CreateSubjectTree();

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void TheTemplateNameFitsWithinTwoHundredAndFiftyFiveBytes()
        {
            var planner = BuildPlanner(root);
            var subject = Subject(subjectPath, artist: new string('A', 400), title: "Title");

            var result = planner.Plan(subject, FileActionVerb.Rename, null, Now);

            var fileName = Path.GetFileName(result.Plan!.To);
            Assert.True(Encoding.UTF8.GetByteCount(fileName) <= 255);
        }
    }

    // T379 review round 2 item 2 — the truncated template is re-validated as an enforced
    // postcondition: a source extension long enough that even a fully-truncated artist/title (both
    // empty) still overflows 255 bytes must refuse, never silently plan an over-length name.
    public sealed class ScenarioAnExtensionTooLongToFitEvenAfterTruncationIsRefused : IDisposable
    {
        readonly string root;
        readonly string subjectPath;

        public ScenarioAnExtensionTooLongToFitEvenAfterTruncationIsRefused()
        {
            root = TestMedia.NewTempDir();
            var subjectDir = Path.Combine(root, "a");
            Directory.CreateDirectory(subjectDir);

            // "." + 253 'e' characters = 254 bytes for the extension alone; "x" + that extension is
            // 255 bytes total — right at NAME_MAX, still creatable on disk.
            var longExtension = "." + new string('e', 253);
            subjectPath = Path.Combine(subjectDir, "x" + longExtension);
            File.WriteAllBytes(subjectPath, [1]);
        }

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void TheRuleIsInvalidNameNeverAnOverLengthPlan()
        {
            var planner = BuildPlanner(root);
            var subject = Subject(subjectPath, artist: new string('A', 400), title: "Title");

            var result = planner.Plan(subject, FileActionVerb.Rename, null, Now);

            Assert.Equal(FileActionRule.InvalidName, result.Refusal!.Value.Rule);
        }
    }

    // ---------------------------------------------------------------------
    // T379 review N2 — a symlink cycle is refused, never thrown
    // ---------------------------------------------------------------------

    public sealed class ScenarioASymlinkCycleAsAMoveTargetIsRefused : IDisposable
    {
        readonly string root;
        readonly string subjectPath;
        readonly string cycleLink;

        public ScenarioASymlinkCycleAsAMoveTargetIsRefused()
        {
            (root, _, subjectPath) = CreateSubjectTree();
            cycleLink = Path.Combine(root, "c1");
            var otherLink = Path.Combine(root, "c2");
            Directory.CreateSymbolicLink(cycleLink, otherLink);
            Directory.CreateSymbolicLink(otherLink, cycleLink);
        }

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void TheRuleIsSymlinkEscape()
        {
            var planner = BuildPlanner(root);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Move, cycleLink, Now);

            Assert.Equal(FileActionRule.SymlinkEscape, result.Refusal!.Value.Rule);
        }
    }

    // ---------------------------------------------------------------------
    // T380 review B6 — a move target reached through a symlink is refused, even one that resolves
    // to somewhere still inside the root (gh-#650's own "two catalog paths, one physical file" gap)
    // ---------------------------------------------------------------------

    public sealed class ScenarioMoveTargetIsASymlinkedDirectory : IDisposable
    {
        readonly string root;
        readonly string subjectPath;
        readonly string linkDir;

        public ScenarioMoveTargetIsASymlinkedDirectory()
        {
            (root, _, subjectPath) = CreateSubjectTree();
            var innerDir = Path.Combine(root, "inner");
            Directory.CreateDirectory(innerDir);
            linkDir = Path.Combine(root, "link");
            Directory.CreateSymbolicLink(linkDir, innerDir);
        }

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void TheRuleIsSymlinkedTarget()
        {
            var planner = BuildPlanner(root);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Move, linkDir, Now);

            Assert.Equal(FileActionRule.SymlinkedTarget, result.Refusal!.Value.Rule);
        }
    }

    /// <summary>The companion positive fact (T380 review B6's own "keep a planner fact that a REAL
    /// directory target still plans") — proves <see cref="FileActionRule.SymlinkedTarget"/> only ever
    /// fires for an ACTUAL symlinked directory, never for an ordinary one.</summary>
    public sealed class ScenarioMoveTargetIsARealDirectory : IDisposable
    {
        readonly string root;
        readonly string subjectPath;
        readonly string targetDir;

        public ScenarioMoveTargetIsARealDirectory()
        {
            (root, _, subjectPath) = CreateSubjectTree();
            targetDir = Path.Combine(root, "b");
            Directory.CreateDirectory(targetDir);
        }

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void APlanIsProduced()
        {
            var planner = BuildPlanner(root);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Move, targetDir, Now);

            Assert.False(result.IsRefused);
        }
    }

    // ---------------------------------------------------------------------
    // Root normalisation — trailing separator, and a root that is itself a symlink
    // ---------------------------------------------------------------------

    public sealed class ScenarioRootTrailingSeparatorDoesNotMatter : IDisposable
    {
        readonly string root;
        readonly string subjectPath;

        public ScenarioRootTrailingSeparatorDoesNotMatter() => (root, _, subjectPath) = CreateSubjectTree();

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void ARootWithNoTrailingSeparatorPlansCleanly()
        {
            var planner = BuildPlanner(root);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Rename, null, Now);

            Assert.False(result.IsRefused);
        }

        [Fact]
        public void ARootWithATrailingSeparatorAlsoPlansCleanly()
        {
            var planner = BuildPlanner(root + Path.DirectorySeparatorChar);

            var result = planner.Plan(Subject(subjectPath), FileActionVerb.Rename, null, Now);

            Assert.False(result.IsRefused);
        }
    }

    public sealed class ScenarioARootThatIsItselfASymlinkResolvesOnce : IDisposable
    {
        readonly string realRoot;
        readonly string symlinkRoot;

        public ScenarioARootThatIsItselfASymlinkResolvesOnce()
        {
            (realRoot, _, _) = CreateSubjectTree();
            symlinkRoot = Path.Combine(Path.GetTempPath(), "gw-libtest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateSymbolicLink(symlinkRoot, realRoot);
        }

        public void Dispose()
        {
            Directory.Delete(symlinkRoot);
            Directory.Delete(realRoot, recursive: true);
        }

        [Fact]
        public void ARenamePlanSucceedsThroughTheSymlinkedRoot()
        {
            var subjectPathThroughTheLink = Path.Combine(symlinkRoot, "a", "x.mp3");
            var planner = BuildPlanner(symlinkRoot);

            var result = planner.Plan(Subject(subjectPathThroughTheLink), FileActionVerb.Rename, null, Now);

            Assert.False(result.IsRefused);
        }
    }
}
