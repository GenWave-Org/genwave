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
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Catalog;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;

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
        public async Task KokoroReturnsANewVoiceIdInItsVoicesListing()
        {
            // Given a fresh install of a pack declaring "af_first"/"af_second",
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);
            var install = await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);
            Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());

            // When the live voice listing is asked for again,
            var response = await client.GetAsync("/api/voices");
            var voices = await response.Content.ReadFromJsonAsync<string[]>() ?? [];

            // Then it already carries the new id (AC2) — kokoro rescans its voices directory
            // per request, no restart involved.
            Assert.Contains("af_first", voices);
            Assert.Contains("af_second", voices);
        }
    }

    public sealed class ScenarioReinstallingTheSameSlugUpserts
    {
        [Fact]
        public async Task ASecondInstallOfTheSameSlugSucceedsInsteadOfRefusingAsStock()
        {
            // Given a pack already installed once — its voice ids now live on the shared flat
            // volume, so kokoro's own dynamic /v1/audio/voices listing above reports them as
            // "stock" too (T412's flat layout, see BuildRoutedHandler's own remarks),
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);
            var first = await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);
            Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());

            // When the SAME slug is installed again,
            var second = await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);

            // Then it succeeds as an upsert (F1 regression guard) — never a 409 "used by stock", the
            // pack's own already-installed ids must never be mistaken for a genuine collision.
            Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());
            Assert.Equal(1, store.PackCount);
            Assert.Equal(2, store.UpsertCallCount);
        }

        [Fact]
        public async Task TheReinstalledFilesAreRewrittenWithNoOrphanTmpLeftBehind()
        {
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);
            await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);

            var second = await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);
            Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());

            // The fixture's own manifest/asset bytes are identical both times, so the second
            // install's atomic write REWRITES each .pt file with the same bytes — proven by
            // re-hashing rather than merely "still exists" (the file is genuinely written again,
            // not skipped).
            var firstBytes = await File.ReadAllBytesAsync(Path.Combine(factory.VoicesRoot, "af_first.pt"));
            Assert.Equal(
                Convert.ToHexStringLower(SHA256.HashData(VoicePackInstallFixtures.FirstPtBytes)),
                Convert.ToHexStringLower(SHA256.HashData(firstBytes)));

            // No orphan .tmp sibling survives the atomic rename on either install.
            Assert.Empty(Directory.EnumerateFiles(factory.VoicesRoot, "*.tmp"));
        }

        [Fact]
        public async Task NoPrevSiblingSurvivesASuccessfulReinstall()
        {
            // T413 review round 2 finding B1(b) — a successful re-install displaces the first
            // install's own live file aside to make room for its own atomic rename, then deletes
            // that displaced sibling for good once the DB transaction commits; nothing named
            // `*.prev-*` should ever be left behind by a re-install that actually succeeded.
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);
            await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);

            var second = await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);
            Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());

            Assert.Empty(Directory.EnumerateFiles(factory.VoicesRoot, "*.prev-*"));
        }
    }

    public sealed class ScenarioAFailedReinstallLeavesThePreviousInstallUntouched
    {
        [Fact]
        public async Task ThePreviousInstallsPtBytesSurviveAFailedReinstall()
        {
            // Given a pack already installed once, its .pt files genuinely live on the shared
            // volume,
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);
            var first = await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);
            Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());

            var firstHash = Convert.ToHexStringLower(SHA256.HashData(
                await File.ReadAllBytesAsync(Path.Combine(factory.VoicesRoot, "af_first.pt"))));

            // When a re-install of the SAME slug fails AFTER its own writes displaced the first
            // install's files aside (T413 review round 2 finding B1 — this is the exact scenario the
            // round 2 review reproduced: a re-install's own store failure used to delete the
            // PREVIOUS install's own live bytes while its DB row survived),
            store.ThrowOnUpsert = new InvalidOperationException("simulated 23514");
            var second = await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);
            Assert.Equal(HttpStatusCode.InternalServerError, second.StatusCode);

            // Then the previous install's own bytes are back in place, unchanged — never merely gone.
            var restoredHash = Convert.ToHexStringLower(SHA256.HashData(
                await File.ReadAllBytesAsync(Path.Combine(factory.VoicesRoot, "af_first.pt"))));
            Assert.Equal(firstHash, restoredHash);
        }

        [Fact]
        public async Task TheDbRowFromTheFirstInstallSurvivesAFailedReinstall()
        {
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);
            await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);
            Assert.Equal(1, store.PackCount);

            store.ThrowOnUpsert = new InvalidOperationException("simulated 23514");
            await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);

            // The first install's own row is exactly where it was — never rolled back to zero
            // packs, since it was never touched by the failed re-install attempt.
            Assert.Equal(1, store.PackCount);
        }

        [Fact]
        public async Task NoTmpOrPrevSiblingSurvivesAFailedReinstall()
        {
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);
            await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);

            store.ThrowOnUpsert = new InvalidOperationException("simulated 23514");
            await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);

            Assert.Empty(Directory.EnumerateFiles(factory.VoicesRoot, "*.tmp"));
            Assert.Empty(Directory.EnumerateFiles(factory.VoicesRoot, "*.prev-*"));
        }

        [Fact]
        public async Task ACancelledUpsertDuringAReinstallLeavesThePreviousInstallUntouched()
        {
            // T413 review round 3 finding F1 — before this fix, both unwind paths filtered
            // OperationCanceledException OUT of the catch (`when (ex is not
            // OperationCanceledException)`), so a cancelled upsert — HttpContext.RequestAborted
            // firing on any client disconnect, or Npgsql surfacing command cancellation as the same
            // exception type — escaped with the write-loop's own displaced-aside `.prev-<guid>`
            // copy and the fresh overwrite left in place: the previous install's bytes were gone,
            // replaced by writes the store never committed a row for. The fix unwinds on
            // cancellation exactly like any other store failure, THEN rethrows, so this fake proves
            // the unwind ran before propagating the cancellation — not that the HTTP call itself
            // returns any particular status.
            //
            // T413 review round 4 finding L2 — the reinstall's own asset bytes must genuinely DIFFER
            // from the first install's (mirrors AdPackKindArc's own two-instance idiom in
            // Story393_AdPackKind.cs): a SINGLE factory/client pair would serve the SAME bytes for
            // both installs, so the sha256 asserts below could never fail no matter whether the
            // unwind ran — the previous install's bytes and the "new" bytes would be identical either
            // way. A SECOND, fresh factory instance is also what a real cancelled-reinstall needs —
            // CatalogProxyService's own 15-minute cache means only a cold cache (a fresh app instance)
            // actually re-fetches instead of replaying the FIRST install's own cached asset bytes.
            using var voicesRootDir = new TempDir();
            var voicesRoot = voicesRootDir.Path;
            var store = new FakeVoicePackStore();
            string firstHash, secondHash;
            await using (var firstFactory = new VoicePackInstallWebFactory(
                store, voicesRoot, VoicePackInstallFixtures.FirstPtBytes, VoicePackInstallFixtures.SecondPtBytes))
            {
                var firstClient = await VoicePackInstallWebFactory.LoggedInClientAsync(firstFactory);
                var first = await firstClient.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);
                Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());

                firstHash = Convert.ToHexStringLower(SHA256.HashData(
                    await File.ReadAllBytesAsync(Path.Combine(voicesRoot, "af_first.pt"))));
                secondHash = Convert.ToHexStringLower(SHA256.HashData(
                    await File.ReadAllBytesAsync(Path.Combine(voicesRoot, "af_second.pt"))));
            }

            store.ThrowOnUpsert = new OperationCanceledException("simulated client disconnect mid-upsert");
            await using (var secondFactory = new VoicePackInstallWebFactory(
                store, voicesRoot,
                VoicePackInstallFixtures.SecondInstallFirstPtBytes, VoicePackInstallFixtures.SecondInstallSecondPtBytes))
            {
                var secondClient = await VoicePackInstallWebFactory.LoggedInClientAsync(secondFactory);
                try
                {
                    await secondClient.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);
                }
                catch (OperationCanceledException)
                {
                    // Rethrowing past the action (rather than answering with a 500) is the fix's
                    // own point — the client genuinely disconnected, so there is no one left to
                    // answer. TestServer resurfaces that unhandled exception at the call site;
                    // either way, the disk/DB state asserted below is what this fact pins.
                }
            }

            var restoredFirstHash = Convert.ToHexStringLower(SHA256.HashData(
                await File.ReadAllBytesAsync(Path.Combine(voicesRoot, "af_first.pt"))));
            var restoredSecondHash = Convert.ToHexStringLower(SHA256.HashData(
                await File.ReadAllBytesAsync(Path.Combine(voicesRoot, "af_second.pt"))));
            Assert.Equal(firstHash, restoredFirstHash);
            Assert.Equal(secondHash, restoredSecondHash);
            Assert.Equal(1, store.PackCount);
            Assert.Empty(Directory.EnumerateFiles(voicesRoot, "*.tmp"));
            Assert.Empty(Directory.EnumerateFiles(voicesRoot, "*.prev-*"));
        }
    }

    public sealed class ScenarioAStoreFailureAfterTheWritesOrphansNothing
    {
        [Fact]
        public async Task AStoreFailureAfterTheWritesReturns500WithAGenericBody()
        {
            // Given a store that fails AFTER the .pt writes would already have landed on disk —
            // e.g. db/45's own `check (engine in ('kokoro'))` tripping on a non-kokoro engine that
            // reached the store (F2's own reachability note; this fake throws directly rather than
            // standing up a real 23514 to prove the CONTROLLER'S unwind, independent of which
            // SQLSTATE triggered it),
            var store = new FakeVoicePackStore { ThrowOnUpsert = new InvalidOperationException("simulated 23514") };
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            var response = await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);
            var body = await response.Content.ReadAsStringAsync();

            // Then it is a generic 500 — no internal detail (F15.7), never the store's own
            // exception text.
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.DoesNotContain("simulated 23514", body, StringComparison.Ordinal);
            Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task NoPtFileSurvivesUnderTheVoicesRootAfterAStoreFailure()
        {
            var store = new FakeVoicePackStore { ThrowOnUpsert = new InvalidOperationException("simulated 23514") };
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);

            await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);

            // F2 — the store failure unwinds every file this attempt itself wrote (no orphan on the
            // shared volume kokoro would otherwise serve with no backing DB row).
            Assert.False(File.Exists(Path.Combine(factory.VoicesRoot, "af_first.pt")));
            Assert.False(File.Exists(Path.Combine(factory.VoicesRoot, "af_second.pt")));
        }

        [Fact]
        public async Task NoTmpSiblingSurvivesAStoreFailureEither()
        {
            var store = new FakeVoicePackStore { ThrowOnUpsert = new InvalidOperationException("simulated 23514") };
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);

            await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);

            Assert.Empty(Directory.EnumerateFiles(factory.VoicesRoot, "*.tmp"));
        }

        [Fact]
        public async Task TheStoreIsNeverLeftHoldingAPackRow()
        {
            var store = new FakeVoicePackStore { ThrowOnUpsert = new InvalidOperationException("simulated 23514") };
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);

            await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);

            Assert.Equal(0, store.PackCount);
        }
    }

    public sealed class ScenarioInstallWritesPtBytesToTheVolume
    {
        [Fact]
        public async Task EveryVoicesPtFileLandsAtTheExpectedPath()
        {
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);

            var install = await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);
            Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());

            // AC3 — the flat layout (T412 ruling): every .pt lands directly at
            // <Packs:VoicesRoot>/<voiceId>.pt, never under a per-pack subfolder.
            Assert.True(File.Exists(Path.Combine(factory.VoicesRoot, "af_first.pt")));
            Assert.True(File.Exists(Path.Combine(factory.VoicesRoot, "af_second.pt")));
        }

        [Fact]
        public async Task EveryPtFilesSha256MatchesTheManifestsPin()
        {
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);

            var install = await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);
            Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());

            // AC3 — every byte written is exactly what the catalog index pinned, never merely
            // "a file exists" — proven against the SAME sha256 the fixture's own index declares.
            var firstBytes = await File.ReadAllBytesAsync(Path.Combine(factory.VoicesRoot, "af_first.pt"));
            var secondBytes = await File.ReadAllBytesAsync(Path.Combine(factory.VoicesRoot, "af_second.pt"));
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(VoicePackInstallFixtures.FirstPtBytes)), Convert.ToHexStringLower(SHA256.HashData(firstBytes)));
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(VoicePackInstallFixtures.SecondPtBytes)), Convert.ToHexStringLower(SHA256.HashData(secondBytes)));
        }
    }

    public sealed class ScenarioInstallPersistsMetadata
    {
        [Fact]
        public async Task StationVoicePackHoldsExactlyOneRowKeyedBySlug()
        {
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);

            var install = await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);
            Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());

            // AC4 — exactly one pack row, keyed by the catalog slug.
            Assert.Equal(1, store.PackCount);
        }

        [Fact]
        public async Task StationVoicePackVoiceHoldsOneRowPerManifestVoice()
        {
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);

            var install = await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);
            Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());

            // AC4 — one voice_pack_voice row per manifest voice, in manifest order.
            var voices = store.TryGetVoices(VoicePackInstallFixtures.InstallSlug);
            Assert.NotNull(voices);
            Assert.Equal(["af_first", "af_second"], voices!.Select(v => v.VoiceId));
        }
    }

    public sealed class ScenarioTheNewVoiceIsLiveWithoutARestart
    {
        [Fact(Skip = "pending: T421 — AC5 (kokoro rescan-per-request wiring is proven above by KokoroReturnsANewVoiceIdInItsVoicesListing; render-time voice selection is T421's own concern)")]
        public void TheNextRenderRequestNamingThePackVoiceReceivesAudio() { }
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioABrokenPackRefusesCleanly
    {
        [Fact]
        public async Task ANoBytesAreWrittenWhenAPtSha256Mismatches()
        {
            // Given a pack whose index PINS the real hash of "af_broken.pt", but whose asset
            // ROUTE serves corrupted bytes instead (a mid-transport tamper/corruption stand-in),
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.BrokenSlug}/install", null);

            // Then nothing was ever written (AC6) — fail-closed integrity verification.
            Assert.False(File.Exists(Path.Combine(factory.VoicesRoot, "af_broken.pt")));
        }

        [Fact]
        public async Task StationVoicePackIsUnchangedOnRefusal()
        {
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);

            await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.BrokenSlug}/install", null);

            // AC6 — all-or-nothing: no row for the broken pack was ever upserted.
            Assert.Equal(0, store.PackCount);
        }

        [Fact]
        public async Task TheResponseIsA502IntegrityProblemDetails()
        {
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.BrokenSlug}/install", null);
            var body = await response.Content.ReadAsStringAsync();

            // AC6 — the catalog transport's own integrity mapping (CatalogInstallShell's
            // WithheldProblem): 502, never a 400 client-input error.
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Contains("Voice pack unavailable.", body, StringComparison.Ordinal);
            Assert.Contains("integrity check", body, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The serializer's own reject arms (mirrors <c>Story393_AdPackKind.cs</c>'s own
    /// <c>ScenarioTheManifestSerializerCapsRejectHonestly</c> idiom one pack-kind over) — pure,
    /// in-process, no DB/HTTP: drives <see cref="CatalogVoicePackManifestSerializer.Deserialize"/>
    /// directly against SPEC F164.3's own synthetic/sourceRef gate, the voice-id shape/length fence
    /// (SPEC F166.4, PLAN T417's <c>SettingValidator.VoiceIdFormat</c>, reused here rather than
    /// re-declared), the duplicate-id fence, and the <see cref="CatalogVoicePackManifestSerializer.MaxVoicesPerPack"/>
    /// cap — every one of these gates degrades the WHOLE manifest to <see langword="null"/>, the same
    /// all-or-nothing posture the happy-path Scenarios above install against.
    /// </summary>
    public sealed class ScenarioTheManifestSerializerCapsRejectHonestly
    {
        static string ManifestJson(
            string voicesArrayJson, bool synthetic = true, string? sourceRef = null, string preview = "test-pack.preview.mp3",
            string engine = "kokoro") =>
            $$"""
            { "packName": "Test Pack", "engine": "{{engine}}", "synthetic": {{(synthetic ? "true" : "false")}},
              "sourceRef": {{(sourceRef is null ? "null" : $"\"{sourceRef}\"")}},
              "preview": "{{preview}}",
              "voices": {{voicesArrayJson}} }
            """;

        static string Voice(string voiceId) => $$"""{ "voiceId": "{{voiceId}}" }""";

        [Fact]
        public void SyntheticFalseFailsToParse()
        {
            // SPEC F164.3 — synthetic must be DECLARED true; an explicit false is refused exactly
            // like an omitted field, never silently accepted as "not synthetic, but fine".
            var manifest = CatalogVoicePackManifestSerializer.Deserialize(
                ManifestJson($"[{Voice("af_test")}]", synthetic: false));

            Assert.Null(manifest);
        }

        [Fact]
        public void ANonNullSourceRefFailsToParse()
        {
            // SPEC F164.3 — sourceRef must be absent or JSON null; ANY other value (even a plausible
            // attribution URL) fails the whole manifest.
            var manifest = CatalogVoicePackManifestSerializer.Deserialize(
                ManifestJson($"[{Voice("af_test")}]", sourceRef: "https://example.test/voice-origin"));

            Assert.Null(manifest);
        }

        [Fact]
        public void AMissingPreviewFailsToParse()
        {
            var manifest = CatalogVoicePackManifestSerializer.Deserialize(
                $$"""
                { "packName": "Test Pack", "engine": "kokoro", "synthetic": true, "sourceRef": null,
                  "voices": [{{Voice("af_test")}}] }
                """);

            Assert.Null(manifest);
        }

        [Theory]
        [InlineData("../x")]
        [InlineData("a/b")]
        [InlineData("AF_TEST")]
        public void ABadlyShapedVoiceIdFailsToParse(string voiceId)
        {
            // The voice id becomes a bare filesystem path segment at install (PLAN T413's own flat
            // `<voiceId>.pt` layout) — a traversal segment, an embedded separator, or an uppercase
            // character (SettingValidator.VoiceIdFormat is lowercase-only) all fail the whole
            // manifest, never merely that one voice.
            var manifest = CatalogVoicePackManifestSerializer.Deserialize(ManifestJson($"[{Voice(voiceId)}]"));

            Assert.Null(manifest);
        }

        [Fact]
        public void A64CharacterVoiceIdIsAccepted()
        {
            // The T417-reviewer-mandated boundary (CatalogVoicePackManifestSerializer.MaxVoiceIdLength)
            // — exactly at the cap parses cleanly.
            var voiceId = "a" + new string('f', CatalogVoicePackManifestSerializer.MaxVoiceIdLength - 1);
            Assert.Equal(CatalogVoicePackManifestSerializer.MaxVoiceIdLength, voiceId.Length);

            var manifest = CatalogVoicePackManifestSerializer.Deserialize(ManifestJson($"[{Voice(voiceId)}]"));

            Assert.NotNull(manifest);
        }

        [Fact]
        public void A65CharacterVoiceIdFailsToParse()
        {
            // One character OVER the same cap fails the whole manifest — the boundary's other side.
            var voiceId = "a" + new string('f', CatalogVoicePackManifestSerializer.MaxVoiceIdLength);
            Assert.Equal(CatalogVoicePackManifestSerializer.MaxVoiceIdLength + 1, voiceId.Length);

            var manifest = CatalogVoicePackManifestSerializer.Deserialize(ManifestJson($"[{Voice(voiceId)}]"));

            Assert.Null(manifest);
        }

        [Fact]
        public void ADuplicateVoiceIdWithinOneManifestFailsToParse()
        {
            var manifest = CatalogVoicePackManifestSerializer.Deserialize(
                ManifestJson($"[{Voice("af_test")}, {Voice("af_test")}]"));

            Assert.Null(manifest);
        }

        [Fact]
        public void MoreThanTheVoiceCountCapFailsToParse()
        {
            var voices = string.Join(
                ", ", Enumerable.Range(0, CatalogVoicePackManifestSerializer.MaxVoicesPerPack + 1).Select(i => Voice($"af_test{i}")));

            var manifest = CatalogVoicePackManifestSerializer.Deserialize(ManifestJson($"[{voices}]"));

            Assert.Null(manifest);
        }

        [Fact]
        public void MalformedJsonDegradesToNullNeverThrows()
        {
            var manifest = CatalogVoicePackManifestSerializer.Deserialize("not json at all");

            Assert.Null(manifest);
        }

        [Fact]
        public void AnEngineOutsideTheClosedSetStillParsesCleanly()
        {
            // T413 review round 2 finding B2 — reverses round 1 finding F2's own narrowing: engine
            // is a SHAPE gate only here now (a safe lowercase token), never closed-set membership.
            // Whether "piper" is actually SUPPORTED is Api.VoicePackController's own closed-set gate
            // (SupportedEngines, mirroring db/45's `check (engine in ('kokoro'))`) — see
            // Story396_VoicePackWrongEngineRefused.cs's own HTTP-level fact for that refusal, which
            // this manifest must reach unparsed-null-free in order to exercise.
            var manifest = CatalogVoicePackManifestSerializer.Deserialize(
                ManifestJson($"[{Voice("af_test")}]", engine: "piper"));

            Assert.NotNull(manifest);
            Assert.Equal("piper", manifest.Engine);
        }

        [Fact]
        public void AnUppercaseEngineFailsToParse()
        {
            // The shape gate (EngineFormat) is lowercase-only, so "Kokoro" fails to parse even
            // though "kokoro" is the one engine this station actually runs — a SHAPE failure, never
            // the controller's own closed-set one.
            var manifest = CatalogVoicePackManifestSerializer.Deserialize(
                ManifestJson($"[{Voice("af_test")}]", engine: "Kokoro"));

            Assert.Null(manifest);
        }

        [Fact]
        public void AnEngineOverTheLengthCapFailsToParse()
        {
            // F6 — defense in depth: an over-length engine token fails outright rather than being
            // echoed anywhere, even though nothing this long could ever equal "kokoro" and pass the
            // closed-set check above either.
            var engine = new string('k', CatalogVoicePackManifestSerializer.MaxEngineLength + 1);
            var manifest = CatalogVoicePackManifestSerializer.Deserialize(
                ManifestJson($"[{Voice("af_test")}]", engine: engine));

            Assert.Null(manifest);
        }
    }

    // ---------------------------------------------------------------------
    // LISTING ROUTE (PLAN T418, STORY-397 — GET /api/voice-packs feeds the catalog shelf's own
    // "Installed" chip and detail panel; see InstalledPackSummaryDto's own remarks for the full
    // contract).
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheInstalledPacksListing
    {
        [Fact]
        public async Task ReturnsEveryInstalledPacksSlugAndDisplayName()
        {
            // Given a pack installed through the normal install route (so its manifest lives in
            // the store exactly as a real install would leave it, not a hand-built fixture row),
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);
            var install = await client.PostAsync($"/api/voice-packs/{VoicePackInstallFixtures.InstallSlug}/install", null);
            Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());

            // When the listing route is asked for the installed packs,
            var response = await client.GetAsync("/api/voice-packs");

            // Then it reports 200 with exactly that one slug and the manifest's own display name —
            // never the voice roster, engine, or preview file (this route's whole job is "which
            // slugs are installed", the shelf already has the rest off the catalog entry itself).
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var summaries = await response.Content.ReadFromJsonAsync<InstalledPackSummaryDto[]>() ?? [];
            var summary = Assert.Single(summaries);
            Assert.Equal(VoicePackInstallFixtures.InstallSlug, summary.Slug);
            Assert.Equal("Test Pack", summary.PackName);
        }

        [Fact]
        public async Task AMalformedStoredDefinitionListsWithPackNameFallingBackToTheSlug()
        {
            // Given a row whose stored `definition` fails to re-parse (seeded directly through the
            // store, bypassing Install's own manifest validation — a hand-edited or
            // migration-corrupted row, not something the install route itself could ever write —
            // InstalledPackSummaryDto's own remarks: this listing never drops such a row the way
            // AttributionsController's own skip-and-log posture does),
            var store = new FakeVoicePackStore();
            await store.UpsertAsync(
                VoicePackInstallFixtures.MalformedSlug, "kokoro", "{}", "test", [], CancellationToken.None);
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);

            // When the listing route is asked for the installed packs,
            var response = await client.GetAsync("/api/voice-packs");

            // Then it still reports 200 with that row, its display name falling back to the slug
            // itself rather than the row vanishing or the request 500ing.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var summaries = await response.Content.ReadFromJsonAsync<InstalledPackSummaryDto[]>() ?? [];
            var summary = Assert.Single(summaries);
            Assert.Equal(VoicePackInstallFixtures.MalformedSlug, summary.Slug);
            Assert.Equal(VoicePackInstallFixtures.MalformedSlug, summary.PackName);
        }

        [Fact]
        public async Task DoesNotExistWithAdminDisabled()
        {
            // SPEC F61.2 (STORY-166) — the admin kill switch applies to every AdminSurface route,
            // this new listing route included: 404, not merely 401, with the plane switched off.
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackInstallWebFactory(store, adminEnabled: false);
            var client = factory.CreateClient();

            var response = await client.GetAsync("/api/voice-packs");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    // ---------------------------------------------------------------------
    // ASSET CONTENT TYPE (T418 rider — GET /api/catalog/entries/{slug}/assets/{file} names the
    // preview clip's real MIME type; see CatalogController.AssetContentType's own remarks for the
    // full switch this fact pins one arm of).
    // ---------------------------------------------------------------------

    public sealed class ScenarioThePreviewAssetIsServedAsAudio
    {
        [Fact]
        public async Task ThePreviewMp3ServesWithAudioMpegContentType()
        {
            // Given the catalog's own index naming this pack's real preview mp3 (no install
            // needed first — the asset route resolves straight off the fetched/cached index, the
            // same way ScenarioAssetsStreamThroughTheGuardedDoor's woff2 facts do for a font pack),
            var store = new FakeVoicePackStore();
            await using var factory = new VoicePackInstallWebFactory(store);
            var client = await VoicePackInstallWebFactory.LoggedInClientAsync(factory);

            // When the preview asset is fetched through the real production asset route,
            var response = await client.GetAsync(
                $"/api/catalog/entries/{VoicePackInstallFixtures.InstallSlug}/assets/{VoicePackInstallFixtures.InstallSlug}.preview.mp3");

            // Then it serves 200 as audio/mpeg — not the generic binary fallback a browser would
            // have to sniff around — with the exact hash-verified preview bytes.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("audio/mpeg", response.Content.Headers.ContentType?.MediaType);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(VoicePackInstallFixtures.PreviewBytes, bytes);
        }
    }
}

/// <summary>
/// A two-entry fake catalog origin (mirrors <c>FontPackInstallWebFactory</c>'s own shape): a clean
/// two-voice pack ("install-test-pack") for the happy-path facts, and a pack whose asset route
/// deliberately serves bytes that do NOT match its own index-pinned sha256 ("broken-pack") for the
/// hash-mismatch sad path — plus a fresh, per-instance temp <c>Packs:VoicesRoot</c>, and a Kokoro
/// <c>GET /v1/audio/voices</c> route that dynamically lists whatever <c>.pt</c> files that root
/// actually holds (the same "kokoro rescans, no restart" idiom <c>Story398_VoiceIdCollisionRefused.cs</c>
/// uses for its own rename fact).
/// </summary>
file sealed class VoicePackInstallWebFactory : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story395-voicepack-install";

    readonly FakeVoicePackStore store;
    readonly FakeHttpMessageHandler handler;
    readonly TempDir? ownedVoicesRoot;
    readonly bool adminEnabled;

    public string VoicesRoot { get; }

    /// <summary><paramref name="adminEnabled"/> (PLAN T418) lets this file's own installed-packs
    /// listing scenario reuse this factory for its kill-switch fact rather than standing up a
    /// parallel one — every other caller leaves it at its default (true, the app's own default).</summary>
    public VoicePackInstallWebFactory(FakeVoicePackStore store, bool adminEnabled = true)
        : this(store, new TempDir(), VoicePackInstallFixtures.FirstPtBytes, VoicePackInstallFixtures.SecondPtBytes, adminEnabled)
    {
    }

    /// <summary>
    /// The reinstall-with-different-content shape (T413 review round 4 finding L2 — mirrors
    /// <c>AdPackInstallWebFactory</c>'s own two-instance idiom in <c>Story393_AdPackKind.cs</c>): a
    /// SECOND factory instance over the SAME <paramref name="voicesRoot"/> and the SAME
    /// <paramref name="store"/> a FIRST factory already installed into — a fresh app instance is what
    /// actually bypasses <c>CatalogProxyService</c>'s 15-minute cache, so this instance's own fake
    /// handler's DIFFERENT <c>.pt</c> bytes are genuinely what gets fetched and written. Never deletes
    /// <paramref name="voicesRoot"/> on dispose — the caller owns that directory's lifetime across
    /// both instances.
    /// </summary>
    public VoicePackInstallWebFactory(FakeVoicePackStore store, string voicesRoot, byte[] firstPtBytes, byte[] secondPtBytes)
        : this(store, voicesRoot, firstPtBytes, secondPtBytes, ownedVoicesRoot: null, adminEnabled: true)
    {
    }

    /// <summary>The self-creating constructor's own <see cref="TempDir"/> flows through as BOTH the
    /// resolved <paramref name="ownedRoot"/> path and the disposer this instance owns — created once,
    /// here, never twice.</summary>
    VoicePackInstallWebFactory(
        FakeVoicePackStore store, TempDir ownedRoot, byte[] firstPtBytes, byte[] secondPtBytes, bool adminEnabled)
        : this(store, ownedRoot.Path, firstPtBytes, secondPtBytes, ownedRoot, adminEnabled)
    {
    }

    VoicePackInstallWebFactory(
        FakeVoicePackStore store, string voicesRoot, byte[] firstPtBytes, byte[] secondPtBytes, TempDir? ownedVoicesRoot,
        bool adminEnabled)
    {
        this.store = store;
        VoicesRoot = voicesRoot;
        this.ownedVoicesRoot = ownedVoicesRoot;
        this.adminEnabled = adminEnabled;
        handler = VoicePackInstallFixtures.BuildRoutedHandler(voicesRoot, firstPtBytes, secondPtBytes);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Admin:Enabled", adminEnabled ? "true" : "false");
        builder.UseSetting("Community:CatalogIndexUrl", VoicePackInstallFixtures.IndexUrl);
        builder.UseSetting("Packs:VoicesRoot", VoicesRoot);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(handler));

            services.RemoveAll<IVoicePackStore>();
            services.AddSingleton<IVoicePackStore>(store);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ownedVoicesRoot?.Dispose();
        }

        base.Dispose(disposing);
    }

    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

file static class VoicePackInstallFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/voice-install-index.json";
    const string Origin = "https://catalog.test/repo/";
    // Tts:Endpoint under the "Development" ASP.NET environment this factory uses (see
    // appsettings.Development.json) is "http://localhost:8880", not the production default.
    const string KokoroVoicesUrl = "http://localhost:8880/v1/audio/voices";

    public const string InstallSlug = "install-test-pack";
    public const string BrokenSlug = "broken-pack";
    // T418 review round 1 finding O3 — a row seeded with a stored definition that fails to
    // re-parse, never installed through the real route (see ScenarioTheInstalledPacksListing's own
    // AMalformedStoredDefinitionListsWithPackNameFallingBackToTheSlug).
    public const string MalformedSlug = "malformed-voice-pack";

    public static readonly byte[] FirstPtBytes = Encoding.UTF8.GetBytes("first-voice-bytes-story395");
    public static readonly byte[] SecondPtBytes = Encoding.UTF8.GetBytes("second-voice-bytes-story395");
    // A reinstall's own DIFFERENT content (T413 review round 4 finding L2) — genuinely different
    // bytes from FirstPtBytes/SecondPtBytes so a sha256 assertion pinning "the previous install's
    // bytes survived" can actually fail when they don't (see
    // ACancelledUpsertDuringAReinstallLeavesThePreviousInstallUntouched, the one fact that uses
    // these).
    public static readonly byte[] SecondInstallFirstPtBytes = Encoding.UTF8.GetBytes("reinstall-first-voice-bytes-story395");
    public static readonly byte[] SecondInstallSecondPtBytes = Encoding.UTF8.GetBytes("reinstall-second-voice-bytes-story395");
    static readonly byte[] RealBrokenPtBytes = Encoding.UTF8.GetBytes("the-real-broken-voice-bytes");
    static readonly byte[] CorruptedBrokenPtBytes = Encoding.UTF8.GetBytes("NOT-the-bytes-the-index-pinned");
    // The index's own "bytes" field for af_broken.pt below is deliberately
    // CorruptedBrokenPtBytes.Length, NOT RealBrokenPtBytes.Length: CatalogProxyService bounds its
    // streamed read at min(declared bytes, per-kind cap) BEFORE it ever compares the hash
    // (CatalogProxyService.FetchAndVerifyAssetAsync), so an undersized declared length would trip
    // "exceeded its size limit" first and this fixture would never reach the hash-mismatch path it
    // exists to prove. Only the pinned sha256 is wrong here — the declared size is honest.
    // Public (T418 rider) — ScenarioThePreviewAssetIsServedAsAudio compares the real asset route's
    // response body against these exact bytes.
    public static readonly byte[] PreviewBytes = Encoding.UTF8.GetBytes("fake-preview-bytes-for-story395");

    static string ManifestJson(string slug, params string[] voiceIds) => $$"""
        { "packName": "Test Pack", "engine": "kokoro", "synthetic": true, "sourceRef": null,
          "preview": "{{slug}}.preview.mp3",
          "voices": [ {{string.Join(", ", voiceIds.Select(id => $$"""{ "voiceId": "{{id}}" }"""))}} ] }
        """;

    const string MetaJson = """
        {"author":"Test Fixture","description":"A voice pack for the install specs.","audience":"everyone"}
        """;

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    // firstPtBytes/secondPtBytes are parameters (T413 review round 4 finding L2), NOT the
    // FirstPtBytes/SecondPtBytes constants directly — a reinstall fact needs the index's own pinned
    // hashes to match WHATEVER bytes that instance's handler actually serves, so a SECOND
    // BuildRoutedHandler call can serve genuinely different, still-verifying content for the SAME
    // voice ids (see VoicePackInstallWebFactory's own two-instance constructor).
    static string IndexJson(byte[] firstPtBytes, byte[] secondPtBytes) => $$"""
        { "generatedAt": "2026-09-06", "entries": [
          { "slug": "{{InstallSlug}}", "kind": "voice-pack", "audience": "everyone",
            "manifest": { "path": "entries/{{InstallSlug}}/{{InstallSlug}}.voice-pack.json", "sha256": "{{Sha256Hex(ManifestJson(InstallSlug, "af_first", "af_second"))}}" },
            "meta": { "path": "entries/{{InstallSlug}}/{{InstallSlug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
            "assets": [
              { "path": "entries/{{InstallSlug}}/af_first.pt", "sha256": "{{Sha256Hex(firstPtBytes)}}", "bytes": {{firstPtBytes.Length}} },
              { "path": "entries/{{InstallSlug}}/af_second.pt", "sha256": "{{Sha256Hex(secondPtBytes)}}", "bytes": {{secondPtBytes.Length}} },
              { "path": "entries/{{InstallSlug}}/{{InstallSlug}}.preview.mp3", "sha256": "{{Sha256Hex(PreviewBytes)}}", "bytes": {{PreviewBytes.Length}} }
            ] },
          { "slug": "{{BrokenSlug}}", "kind": "voice-pack", "audience": "everyone",
            "manifest": { "path": "entries/{{BrokenSlug}}/{{BrokenSlug}}.voice-pack.json", "sha256": "{{Sha256Hex(ManifestJson(BrokenSlug, "af_broken"))}}" },
            "meta": { "path": "entries/{{BrokenSlug}}/{{BrokenSlug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
            "assets": [
              { "path": "entries/{{BrokenSlug}}/af_broken.pt", "sha256": "{{Sha256Hex(RealBrokenPtBytes)}}", "bytes": {{CorruptedBrokenPtBytes.Length}} },
              { "path": "entries/{{BrokenSlug}}/{{BrokenSlug}}.preview.mp3", "sha256": "{{Sha256Hex(PreviewBytes)}}", "bytes": {{PreviewBytes.Length}} }
            ] } ] }
        """;

    /// <summary>
    /// Serves both fixture packs' own documents/assets — <c>broken-pack</c>'s own
    /// <c>af_broken.pt</c> route deliberately serves <see cref="CorruptedBrokenPtBytes"/> while the
    /// index still pins <see cref="RealBrokenPtBytes"/>'s own hash, proving fail-closed integrity
    /// verification (mirrors <c>FontPackInstallFixtures</c>'s own
    /// <c>AHashMismatchRefusesFailClosedWithNothingStored</c> precedent) — plus a Kokoro
    /// <c>GET /v1/audio/voices</c> that dynamically lists whatever <c>.pt</c> files
    /// <paramref name="voicesRoot"/> actually holds at request time. <paramref name="firstPtBytes"/>/
    /// <paramref name="secondPtBytes"/> let a reinstall fact serve genuinely different content for the
    /// SAME <c>af_first</c>/<c>af_second</c> voice ids (T413 review round 4 finding L2) — every other
    /// caller just passes <see cref="FirstPtBytes"/>/<see cref="SecondPtBytes"/> straight through.
    /// </summary>
    public static FakeHttpMessageHandler BuildRoutedHandler(string voicesRoot, byte[] firstPtBytes, byte[] secondPtBytes)
    {
        var routes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [IndexUrl] = IndexJson(firstPtBytes, secondPtBytes),
            [Origin + $"entries/{InstallSlug}/{InstallSlug}.voice-pack.json"] = ManifestJson(InstallSlug, "af_first", "af_second"),
            [Origin + $"entries/{InstallSlug}/{InstallSlug}.meta.json"] = MetaJson,
            [Origin + $"entries/{BrokenSlug}/{BrokenSlug}.voice-pack.json"] = ManifestJson(BrokenSlug, "af_broken"),
            [Origin + $"entries/{BrokenSlug}/{BrokenSlug}.meta.json"] = MetaJson,
        };
        var assetBytesByUrl = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [Origin + $"entries/{InstallSlug}/af_first.pt"] = firstPtBytes,
            [Origin + $"entries/{InstallSlug}/af_second.pt"] = secondPtBytes,
            [Origin + $"entries/{InstallSlug}/{InstallSlug}.preview.mp3"] = PreviewBytes,
            // Deliberately corrupted — the index above still pins RealBrokenPtBytes's own hash.
            [Origin + $"entries/{BrokenSlug}/af_broken.pt"] = CorruptedBrokenPtBytes,
            [Origin + $"entries/{BrokenSlug}/{BrokenSlug}.preview.mp3"] = PreviewBytes,
        };

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;

            if (string.Equals(absoluteUri, KokoroVoicesUrl, StringComparison.Ordinal))
            {
                var installedVoiceIds = System.IO.Directory.Exists(voicesRoot)
                    ? System.IO.Directory.EnumerateFiles(voicesRoot, "*.pt").Select(f => Path.GetFileNameWithoutExtension(f))
                    : [];
                var voices = installedVoiceIds.ToArray();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { voices }),
                });
            }

            if (assetBytesByUrl.TryGetValue(absoluteUri, out var assetBytes))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(assetBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}
