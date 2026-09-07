// STORY-395 — I install a voice pack and its voices go live without a restart (SPEC F164.1/.5, F166 · PLAN T413)
//
// Pending until PLAN T413 lands. Bodies use Assert.Fail so the /build-loop turns each Fact green as
// the surface builds; the Feature/Scenario/Specification shape is the contract until then.
//
// The T412 facts below (ScenarioTheVoicesVolumeIsSharedAndSeeded + ScenarioApiWritesKokoroReads's
// first two) are wired against real compose.yaml content: mount-posture facts read the resolved
// `docker compose config` JSON (same idiom as Gh242_ComposePiperOnlyOverride), and the seed +
// idempotency facts run the ACTUAL `voice-seed` image/command the render carries against a scratch
// Docker volume — never a hand-copied second version of that command that could quietly drift from
// compose.yaml. Deliberately does not assert a stock voice count (kokoro's own baked-in count is not
// this repo's to pin — SPEC F166.2): the seeded volume's file set is compared against the SAME
// image's own listing, so a kokoro version bump changing that count never breaks this spec.

using System.Diagnostics;
using System.Text.Json;

namespace GenWave.Host.Tests.Specs;

public static class FeatureVoicePackInstallGoesLiveWithoutARestart
{
    // ── compose.yaml render + docker-run helpers (T412) ────────────────────

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return dir.FullName;
    }

    static (int ExitCode, string StdOut, string StdErr) RunProcess(
        string fileName,
        string[] args,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
        if (environment is not null)
            foreach (var (key, value) in environment)
                startInfo.Environment[key] = value;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"failed to start {fileName}");
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdOut, stdErr);
    }

    static JsonDocument RenderConfig()
    {
        // Same dummy-secret idiom as Gh242/Story181/Story202: `config` only merges text, no
        // daemon reached. No overlays — the T412 facts below all concern the default topology.
        var env = new Dictionary<string, string>
        {
            ["POSTGRES_PASSWORD"] = "story395-dummy",
            ["LIBRARY_DB_PASSWORD"] = "story395-dummy",
            ["STATION_DB_PASSWORD"] = "story395-dummy",
            ["ICECAST_SOURCE_PASSWORD"] = "story395-dummy",
            ["ICECAST_ADMIN_PASSWORD"] = "story395-dummy",
            ["ADMIN_PASSWORD"] = "story395-dummy",
            ["MEDIA_DIR"] = Path.GetTempPath(),
            ["PUBLIC_HOST"] = "story395.invalid",
            // gh-#249: explicit-but-empty shadows BOTH ambient COMPOSE_PROFILES and a dev
            // box's repo-root .env value, so the render sees the same profile set (none)
            // CI does. Overlay/flag-selected profiles are unaffected — a --profile flag
            // takes precedence over this variable entirely (verified empirically).
            ["COMPOSE_PROFILES"] = "",
        };

        var (exitCode, stdOut, stdErr) = RunProcess(
            "docker",
            ["compose", "-f", "compose.yaml", "config", "--format", "json"],
            env);
        if (exitCode != 0)
            throw new InvalidOperationException($"docker compose config failed (exit {exitCode}): {stdErr}");

        return JsonDocument.Parse(stdOut);
    }

    static readonly Lazy<JsonDocument> Base = new(RenderConfig);

    readonly record struct VolumeMount(string Source, string Target, bool ReadOnly);

    static IReadOnlyList<VolumeMount> VolumeMounts(JsonDocument render, string service)
    {
        var svc = render.RootElement.GetProperty("services").GetProperty(service);
        if (!svc.TryGetProperty("volumes", out var volumes)) return [];

        return volumes.EnumerateArray()
            .Where(v => v.GetProperty("type").GetString() == "volume")
            .Select(v => new VolumeMount(
                v.GetProperty("source").GetString()
                    ?? throw new InvalidOperationException($"{service}: volume mount missing source"),
                v.GetProperty("target").GetString()
                    ?? throw new InvalidOperationException($"{service}: volume mount missing target"),
                v.TryGetProperty("read_only", out var readOnly) && readOnly.GetBoolean()))
            .ToList();
    }

    readonly record struct VoiceSeedSpec(string Image, string? User, string VolumeTarget, string[] Command);

    /// <summary>
    /// Reads voice-seed's actual image/user/mount-target/command straight off the render, so the
    /// container run below always exercises exactly what compose.yaml carries today — never a
    /// second, hand-copied version of the same command that could drift out of sync with it.
    /// </summary>
    static VoiceSeedSpec ReadVoiceSeedSpec(JsonDocument render)
    {
        var svc = render.RootElement.GetProperty("services").GetProperty("voice-seed");
        var image = svc.GetProperty("image").GetString()
            ?? throw new InvalidOperationException("voice-seed: image missing from render");
        var user = svc.TryGetProperty("user", out var userProp) ? userProp.GetString() : null;
        var mount = VolumeMounts(render, "voice-seed").Single();
        var command = svc.GetProperty("command").EnumerateArray()
            .Select(e => e.GetString() ?? throw new InvalidOperationException("voice-seed: command element missing"))
            // `docker compose config`'s own serialization re-escapes a literal "$" back to "$$"
            // (the same escape compose.yaml's source had to use to survive compose's OWN
            // interpolation pass) — undo that here, once, so `docker run` below (which does no
            // interpolation of its own) receives the literal shell script the container actually
            // needs, not a doubled-dollar string that `$$` (the shell's PID variable) would mangle.
            .Select(element => element.Replace("$$", "$", StringComparison.Ordinal))
            .ToArray();
        return new VoiceSeedSpec(image, user, mount.Target, command);
    }

    static string KokoroScanDirectory(JsonDocument render) =>
        VolumeMounts(render, "kokoro").Single().Target;

    static string CreateScratchVolume(string label)
    {
        var name = $"genwave-story395-{label}-{Guid.NewGuid():N}";
        var (exitCode, _, stdErr) = RunProcess("docker", ["volume", "create", name]);
        if (exitCode != 0)
            throw new InvalidOperationException($"docker volume create failed (exit {exitCode}): {stdErr}");
        return name;
    }

    static void RemoveScratchVolume(string name) => RunProcess("docker", ["volume", "rm", "-f", name]);

    /// <summary>Runs a throwaway container: `docker run --rm --network none [--user] [-v]
    /// image command...`. The ONE place gh-#229's rule lives (a new veth on this box kicks the
    /// owner's live stream) — every scratch-container call below goes through here, so nothing
    /// can drift and add a network back by accident.</summary>
    static (int ExitCode, string StdOut, string StdErr) DockerRun(
        string image,
        string? user,
        string? volume,
        string? mountTarget,
        bool readOnly,
        string[] command)
    {
        var args = new List<string> { "run", "--rm", "--network", "none" };
        if (user is not null) { args.Add("--user"); args.Add(user); }
        if (volume is not null)
        {
            args.Add("-v");
            args.Add(readOnly ? $"{volume}:{mountTarget}:ro" : $"{volume}:{mountTarget}");
        }
        args.Add(image);
        args.AddRange(command);

        return RunProcess("docker", args.ToArray());
    }

    /// <summary>Runs the voice-seed image/command against <paramref name="volumeName"/>, mounted
    /// read-write unless <paramref name="readOnlyMount"/> asks for read-only (the fail-loud-path
    /// probe below).</summary>
    static (int ExitCode, string StdOut, string StdErr) RunSeed(VoiceSeedSpec spec, string volumeName, bool readOnlyMount) =>
        DockerRun(spec.Image, spec.User, volumeName, spec.VolumeTarget, readOnlyMount, spec.Command);

    /// <summary>Runs the voice-seed image/command against a scratch volume mounted read-write —
    /// the happy path the seed is meant to succeed at. Throws with the captured stderr if the run
    /// fails, since that always indicates a broken test fixture rather than the behaviour under
    /// test.</summary>
    static void RunSeedOnce(VoiceSeedSpec spec, string volumeName)
    {
        var (exitCode, _, stdErr) = RunSeed(spec, volumeName, readOnlyMount: false);
        if (exitCode != 0)
            throw new InvalidOperationException($"voice-seed container run failed (exit {exitCode}): {stdErr}");
    }

    /// <summary>Runs the voice-seed image/command against a scratch volume mounted READ-ONLY,
    /// proving the fail-loud path (Finding 1 — a mid-loop `cp` failure must exit non-zero, never
    /// swallowed by the loop's own last-iteration exit status). A non-zero exit is the expected
    /// outcome here, so unlike <see cref="RunSeedOnce"/> it never throws on one.</summary>
    static int RunSeedExpectingRefusal(VoiceSeedSpec spec, string volumeName) =>
        RunSeed(spec, volumeName, readOnlyMount: true).ExitCode;

    /// <summary>Lists a directory — or a <c>*.pt</c> glob inside one — from a throwaway container
    /// run from <paramref name="image"/>, optionally with a scratch volume mounted at <paramref
    /// name="mountTarget"/>, as basenames sorted ordinally. Runs as <paramref name="user"/> when
    /// given (voice-seed's own render-derived user) so a listing never silently depends on the
    /// image's default-user umask to even see what the seed, running as a different user, wrote.</summary>
    static string[] ListDirectory(string image, string containerPath, string? scratchVolume = null, string? mountTarget = null, string? user = null)
    {
        // No quotes around containerPath: an unquoted `*` here is deliberate — it lets a caller
        // pass a glob (e.g. "<dir>/*.pt") and have the shell expand it before `ls` ever runs.
        // Path.GetFileName below then turns each result into a basename either way: a plain
        // directory listing is already bare filenames (a no-op), a glob expansion is full paths
        // (stripped to match).
        var (exitCode, stdOut, stdErr) = DockerRun(
            image, user, scratchVolume, mountTarget, readOnly: false,
            command: ["sh", "-c", $"ls -A {containerPath}"]);
        if (exitCode != 0)
            throw new InvalidOperationException($"docker run (ls {containerPath}) failed (exit {exitCode}): {stdErr}");

        return stdOut.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => Path.GetFileName(entry.AsSpan()).ToString())
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Copies exactly one file already present inside <paramref name="image"/> into a
    /// scratch volume mounted READ-WRITE at <paramref name="mountTarget"/> — used to pre-seed a
    /// volume with a single known file before a later read-only run (see
    /// ASeedThatCannotWriteExitsNonZeroRatherThanBeingSwallowed below). Runs as
    /// <paramref name="user"/> (voice-seed's own render-derived user, since a freshly created
    /// named volume is root-owned).</summary>
    static void CopyFileIntoVolume(string image, string? user, string sourcePath, string scratchVolume, string mountTarget)
    {
        var (exitCode, _, stdErr) = DockerRun(
            image, user, scratchVolume, mountTarget, readOnly: false,
            command: ["sh", "-c", $"cp \"{sourcePath}\" \"{mountTarget}/\""]);
        if (exitCode != 0)
            throw new InvalidOperationException($"docker run (cp {sourcePath} -> {mountTarget}) failed (exit {exitCode}): {stdErr}");
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheVoicesVolumeIsSharedAndSeeded
    {
        [Fact]
        [Trait("Category", "Integration")]
        public static void TheVoicesVolumeHoldsKokorosStockVoicesAfterFirstBoot()
        {
            // AC1 — a fresh `up` leaves the `voices` volume holding kokoro's stock .pt files.
            // Compared against the SAME image's own listing (not a hardcoded count): robust to a
            // kokoro version bump changing how many stock voices it ships (SPEC F166.2).
            var render = Base.Value;
            var spec = ReadVoiceSeedSpec(render);
            var volume = CreateScratchVolume("seed");
            try
            {
                RunSeedOnce(spec, volume);

                var seeded = ListDirectory(spec.Image, spec.VolumeTarget, volume, spec.VolumeTarget, spec.User);
                var stock = ListDirectory(spec.Image, $"{KokoroScanDirectory(render)}/*.pt", user: spec.User);

                Assert.NotEmpty(stock);
                Assert.Equal(stock, seeded);
            }
            finally
            {
                RemoveScratchVolume(volume);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void ASecondComposeUpIsANoOp()
        {
            // AC1 — the init container's per-file existence guard means a second run copies
            // nothing new: the volume's file set is unchanged run-over-run.
            var render = Base.Value;
            var spec = ReadVoiceSeedSpec(render);
            var volume = CreateScratchVolume("idempotent");
            try
            {
                RunSeedOnce(spec, volume);
                var afterFirstRun = ListDirectory(spec.Image, spec.VolumeTarget, volume, spec.VolumeTarget, spec.User);

                RunSeedOnce(spec, volume);
                var afterSecondRun = ListDirectory(spec.Image, spec.VolumeTarget, volume, spec.VolumeTarget, spec.User);

                Assert.Equal(afterFirstRun, afterSecondRun);
            }
            finally
            {
                RemoveScratchVolume(volume);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void VoiceSeedUsesTheExactSameImagePinAsKokoro()
        {
            // This repo's law (a hand-synced mirror needs a text-equality pin — SEAMS.md):
            // voice-seed's image is a duplicated literal, not a YAML anchor (compose.yaml carries
            // none today), so nothing but this test stops the two pins drifting apart on a future
            // kokoro version bump.
            var services = Base.Value.RootElement.GetProperty("services");
            var kokoroImage = services.GetProperty("kokoro").GetProperty("image").GetString();
            var voiceSeedImage = services.GetProperty("voice-seed").GetProperty("image").GetString();

            Assert.Equal(kokoroImage, voiceSeedImage);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void ASeedThatCannotWriteExitsNonZeroRatherThanBeingSwallowed()
        {
            // Finding 1 (fail-loud regression guard): a `for`/`||` loop's own exit status is
            // whichever iteration ran LAST — without `set -e`, an early `cp` failure (ENOSPC/EIO
            // on a small appliance is the realistic trigger) would be masked by a later successful
            // iteration, and the container would exit 0 on a partially seeded volume.
            //
            // On a volume that starts COMPLETELY empty this fact can't tell the two apart: every
            // iteration's `[ -e … ]` guard is false, so every `cp` fails — including the last one
            // — and the loop's own exit status is already non-zero whether or not `set -e` is
            // there (round-2 review caught this: deleting `set -e` from the rendered script still
            // exited 1 against an empty read-only volume). The pre-seed below exists solely to
            // make the LAST iteration take the skip branch: copy in (read-write) exactly the
            // alphabetically last stock voice — glob order is sort order, so that's the loop's
            // last iteration — then remount the same volume read-only. Now only the earlier
            // iterations fail; the loop's own exit status would be 0 (the last, skipped, iteration
            // "succeeded"), so a non-zero exit here can only come from `set -e` catching the
            // earlier failure.
            var render = Base.Value;
            var spec = ReadVoiceSeedSpec(render);
            var scanDirectory = KokoroScanDirectory(render);
            var stock = ListDirectory(spec.Image, $"{scanDirectory}/*.pt", user: spec.User);
            var lastStockVoice = stock[^1];
            var volume = CreateScratchVolume("readonly-refuses");
            try
            {
                CopyFileIntoVolume(spec.Image, spec.User, $"{scanDirectory}/{lastStockVoice}", volume, spec.VolumeTarget);

                var exitCode = RunSeedExpectingRefusal(spec, volume);

                Assert.NotEqual(0, exitCode);
            }
            finally
            {
                RemoveScratchVolume(volume);
            }
        }
    }

    public sealed class ScenarioApiWritesKokoroReads
    {
        [Fact]
        [Trait("Category", "Integration")]
        public static void ApiCanWriteToTheSharedVolume()
        {
            // AC2 — api mounts the shared `voices` volume read-write at /voices.
            var mount = VolumeMounts(Base.Value, "api").Single(m => m.Source == "voices");

            Assert.Equal("/voices", mount.Target);
            Assert.False(mount.ReadOnly);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void KokoroCanReadFromTheSharedVolumeButNotWrite()
        {
            // AC2 — kokoro mounts the SAME volume read-only, at its own flat scan directory
            // (kokoro-fastapi v0.6.0 scans /app/api/src/voices/v1_0 non-recursively).
            var mount = VolumeMounts(Base.Value, "kokoro").Single(m => m.Source == "voices");

            Assert.Equal("/app/api/src/voices/v1_0", mount.Target);
            Assert.True(mount.ReadOnly);
        }

        [Fact]
        public void KokoroReturnsANewVoiceIdInItsVoicesListing()
            => Assert.Fail("pending: T413 — AC2");
    }

    public sealed class ScenarioInstallWritesPtBytesToTheVolume
    {
        [Fact]
        public void EveryVoicesPtFileLandsAtTheExpectedPath()
            => Assert.Fail("pending: T413 install writes /voices/{slug}/{voiceId}.pt — AC3");

        [Fact]
        public void EveryPtFilesSha256MatchesTheManifestsPin()
            => Assert.Fail("pending: T413 hash verify — AC3");
    }

    public sealed class ScenarioInstallPersistsMetadata
    {
        [Fact]
        public void StationVoicePackHoldsExactlyOneRowKeyedBySlug()
            => Assert.Fail("pending: T413 upsert — AC4");

        [Fact]
        public void StationVoicePackVoiceHoldsOneRowPerManifestVoice()
            => Assert.Fail("pending: T413 upsert — AC4");
    }

    public sealed class ScenarioTheNewVoiceIsLiveWithoutARestart
    {
        [Fact]
        public void TheNextRenderRequestNamingThePackVoiceReceivesAudio()
            => Assert.Fail("pending: T413 + T421 wire — AC5 (kokoro rescan-per-request, no bounce)");
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioABrokenPackRefusesCleanly
    {
        [Fact]
        public void ANoBytesAreWrittenWhenAPtSha256Mismatches()
            => Assert.Fail("pending: T413 hash-mismatch guard — AC6");

        [Fact]
        public void StationVoicePackIsUnchangedOnRefusal()
            => Assert.Fail("pending: T413 all-or-nothing install — AC6");

        [Fact]
        public void TheResponseIsA502IntegrityProblemDetails()
            => Assert.Fail("pending: T413 catalog transport integrity mapping — AC6");
    }
}
