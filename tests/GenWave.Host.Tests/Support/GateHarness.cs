using System.Net;
using System.Text;
using System.Text.Json;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// The hermetic seat for <c>tools/gate/stack_gate.sh</c> (STORY-444…447): a scratch copy of the
/// repo with a planted <c>.env</c> canary and stubbed <c>setup.sh</c>/<c>launch.sh</c>/<c>migrate.sh</c>,
/// a scripted <c>docker</c>/<c>gh</c>/<c>git</c> on a scratch PATH that log every argv to
/// <c>$GATE_STUB_LOG</c>, and a loopback HTTP server standing in for the api (<c>/health</c>,
/// <c>/api/settings</c>) and icecast (<c>/stream</c>). ffmpeg/ffprobe/jq are the real binaries.
/// <para>
/// Knobs the gate honours (all read from the environment, all optional in production):
/// <c>GATE_API_BASE</c>, <c>GATE_STREAM_URL</c>, <c>GATE_HEALTH_SECS</c>, <c>GATE_ONAIR_SECS</c>,
/// <c>GATE_OUTAGE_SECS</c>, <c>GATE_RECOVERY_SECS</c>, <c>GATE_POLL_SECS</c>, <c>CAPTURE_SECS</c>.
/// The stubs' own knobs are <c>GATE_STUB_*</c>: <c>UP_EXIT</c>, <c>MIGRATE_EXIT</c>,
/// <c>BOOTH_COUNT</c>, <c>BOUNDARY</c>, <c>ONAIR_AFTER</c> (seconds from the reader's first
/// call, reset by <c>stop api</c>/<c>restart engine</c>), <c>NEVER_ONAIR</c>,
/// <c>NEVER_ONAIR_AFTER_STOP</c>, <c>NEVER_ONAIR_AFTER_RESTART</c>, and
/// <c>STREAM_AFTER_RESTART</c> (a WAV the fake station serves once the engine restart is logged). <see cref="ScriptProcess"/> strips only
/// <c>GW_*</c>/<c>SKIP_*</c>/<c>COMPOSE_*</c>, so both families reach the child.
/// </para>
/// </summary>
internal static class GateHarness
{
    public const string Canary = "canary-3f9";
    public const string ScriptPath = "tools/gate/stack_gate.sh";

    /// <summary>Logs argv, cwd + entries and its environment; answers compose like a healthy stack.
    /// <c>up</c> prints the -f/-p view (argv first, COMPOSE_FILE/COMPOSE_PROJECT_NAME as fallback)
    /// and appends the scratch's compose.gate.yaml to <c>$GATE_STUB_LOG.overlay</c>.</summary>
    const string DockerStub = """
        printf '%s\n' "$*" >> "$GATE_STUB_LOG"
        printf 'cwd=%s entries=%s\n' "$PWD" "$(ls -A | tr '\n' ' ')" >> "$GATE_STUB_LOG"
        env >> "$GATE_STUB_LOG.env"
        case "$*" in
          *" version"*) echo "Docker Compose version v2.29.0"; exit 0 ;;
          *" ls"*)      exit 0 ;;
          *" up"*)
            files=""; project=""; prev=""
            for a in "$@"; do
              [ "$prev" = "-f" ] && files="${files:+$files:}$a"
              [ "$prev" = "-p" ] && project="$a"
              prev="$a"
            done
            printf 'up files=%s project=%s\n' "${files:-${COMPOSE_FILE:-}}" "${project:-${COMPOSE_PROJECT_NAME:-}}" >> "$GATE_STUB_LOG"
            [ -f compose.gate.yaml ] && cat compose.gate.yaml >> "$GATE_STUB_LOG.overlay"
            exit "${GATE_STUB_UP_EXIT:-0}" ;;
          *" stop api"*)      rm -f "$GATE_STUB_LOG.onair-first"; touch "$GATE_STUB_LOG.stopped"; exit 0 ;;
          *" restart engine"*) rm -f "$GATE_STUB_LOG.onair-first"; touch "$GATE_STUB_LOG.restarted"; exit 0 ;;
          *" exec -T engine "*)
            stamp="$GATE_STUB_LOG.onair-first"
            [ -f "$stamp" ] || date +%s > "$stamp"
            first=$(cat "$stamp"); now=$(date +%s)
            dark=""
            [ -n "${GATE_STUB_NEVER_ONAIR_AFTER_STOP:-}" ] && [ -f "$GATE_STUB_LOG.stopped" ] && dark=1
            [ -n "${GATE_STUB_NEVER_ONAIR_AFTER_RESTART:-}" ] && [ -f "$GATE_STUB_LOG.restarted" ] && dark=1
            if [ $((now - first)) -ge "${GATE_STUB_ONAIR_AFTER:-0}" ] && [ -z "${GATE_STUB_NEVER_ONAIR:-}$dark" ]; then
              echo 'title="gate-tone",artist="gate",track_id="00000000-0000-4000-8000-000000000042",on_air="true"'
            else
              echo 'title="safe",artist="safe"'
            fi
            exit 0 ;;
          *" exec -T db "*)
            q="$*"; [ -t 0 ] || q="$q $(timeout 1 cat || true)"
            case "$q" in
              *booth_log*) echo "${GATE_STUB_BOOTH_COUNT:-3}" ;;
              *library.media*|*station.settings*) echo "${GATE_STUB_BOUNDARY:-ERROR:  permission denied for schema}" ;;
            esac
            exit 0 ;;
          *" logs"*) echo "stub compose logs"; exit 0 ;;
        esac
        exit 0
        """;

    const string SetupStub = """
        printf 'setup.sh %s GW_ENV_FILE=%s MEDIA_DIR=%s\n' "$*" "${GW_ENV_FILE:-}" "${MEDIA_DIR:-}" >> "$GATE_STUB_LOG"
        printf 'ADMIN_PASSWORD=gate-pass\nPOSTGRES_PASSWORD=stub\nICECAST_SOURCE_PASSWORD=stub\nMEDIA_DIR=%s\n' "${MEDIA_DIR:-/tmp/media}" > "${GW_ENV_FILE:?}"
        """;

    const string LaunchStub = """
        printf 'launch.sh %s\n' "$*" >> "$GATE_STUB_LOG"
        docker compose up -d --wait || exit $?
        ./migrate.sh
        """;

    const string MigrateStub = """
        printf 'migrate.sh %s\n' "$*" >> "$GATE_STUB_LOG"
        exit "${GATE_STUB_MIGRATE_EXIT:-0}"
        """;

    static readonly string[] RealTools =
        ["ffmpeg", "ffprobe", "jq", "timeout", "ls", "cp", "mkdir", "realpath", "basename", "readlink", "xargs", "tee", "rsync", "stat"];

    static readonly string[] Stubbed = ["setup.sh", "launch.sh", "migrate.sh"];

    /// <summary>A scratch PATH: coreutils + the real audio tools + the docker stub (+ any extra stubs).</summary>
    public static string MakeBinDir(IReadOnlyDictionary<string, string>? extraStubs = null)
    {
        var bin = ScriptProcess.MakeBinDir(RealTools);
        ScriptProcess.AddStub(bin, "docker", DockerStub);
        foreach (var (name, body) in extraStubs ?? new Dictionary<string, string>())
            ScriptProcess.AddStub(bin, name, body);
        return bin;
    }

    /// <summary>The caller's checkout: every top-level entry symlinked except <c>.env</c> (planted
    /// with the canary) and <c>.git</c> (a directory the gate must not copy); the three operator
    /// scripts are stubs so the gate's own calls are what the log records.</summary>
    public static string MakeRepoCopy()
    {
        var root = RepoRootLocator.Find(AppContext.BaseDirectory);
        var copy = TempDir.CreateForProcessLifetime();
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            var name = Path.GetFileName(entry);
            if (name is ".env" or ".git" || Stubbed.Contains(name, StringComparer.Ordinal))
                continue;
            if (Directory.Exists(entry))
                Directory.CreateSymbolicLink(Path.Combine(copy, name), entry);
            else
                File.CreateSymbolicLink(Path.Combine(copy, name), entry);
        }
        Directory.CreateDirectory(Path.Combine(copy, ".git"));
        File.WriteAllText(Path.Combine(copy, ".git", "HEAD"), "ref: refs/heads/main\n");
        File.WriteAllText(Path.Combine(copy, ".env"), $"ICECAST_SOURCE_PASSWORD={Canary}\nADMIN_PASSWORD={Canary}\n");
        ScriptProcess.AddStub(copy, "setup.sh", SetupStub);
        ScriptProcess.AddStub(copy, "launch.sh", LaunchStub);
        ScriptProcess.AddStub(copy, "migrate.sh", MigrateStub);
        return copy;
    }

    public sealed record Run(int ExitCode, string StdOut, string StdErr, string[] Calls, string EnvLog,
        string Overlay, string RepoCopy, string ReportDir)
    {
        public string ReportMd => File.Exists(Path.Combine(ReportDir, "gate-report.md"))
            ? File.ReadAllText(Path.Combine(ReportDir, "gate-report.md")) : "";

        public JsonDocument? ReportJson => File.Exists(Path.Combine(ReportDir, "gate-report.json"))
            ? JsonDocument.Parse(File.ReadAllText(Path.Combine(ReportDir, "gate-report.json"))) : null;

        public string? FirstFailure =>
            ReportJson?.RootElement.TryGetProperty("first_failure", out var f) == true ? f.GetString() : null;

        public int IndexOf(string fragment) =>
            Array.FindIndex(Calls, c => c.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>Runs the gate from a fresh repo copy with the shortened budgets and the fake api,
    /// returning the stub log split into calls.</summary>
    public static Run Execute(FakeStation station, string? bin = null,
        IReadOnlyDictionary<string, string>? env = null, params string[] args)
    {
        bin ??= MakeBinDir();
        var copy = MakeRepoCopy();
        var log = Path.Combine(TempDir.CreateForProcessLifetime(), "stub.log");
        var report = Path.Combine(TempDir.CreateForProcessLifetime(), "out");

        var extraEnv = new Dictionary<string, string>
        {
            ["GATE_STUB_LOG"] = log,
            ["GATE_API_BASE"] = station.BaseUrl,
            ["GATE_STREAM_URL"] = station.BaseUrl + "stream",
            ["GATE_HEALTH_SECS"] = "2",
            ["GATE_ONAIR_SECS"] = "4",
            ["GATE_OUTAGE_SECS"] = "1",
            ["GATE_RECOVERY_SECS"] = "4",
            ["GATE_POLL_SECS"] = "1",
            ["CAPTURE_SECS"] = "1",
        };
        foreach (var (k, v) in env ?? new Dictionary<string, string>())
            extraEnv[k] = v;

        var fullArgs = args.Contains("--report", StringComparer.Ordinal) ? args : [.. args, "--report", report];
        station.RestartMarker = log + ".restarted";
        station.StreamAfterRestart = extraEnv.GetValueOrDefault("GATE_STUB_STREAM_AFTER_RESTART");

        var (exit, stdOut, stdErr) = ScriptProcess.Run(
            Path.Combine(copy, ScriptPath), bin, envFile: null, extraEnv, fullArgs);

        return new Run(exit, stdOut, stdErr,
            File.Exists(log) ? File.ReadAllLines(log) : [],
            File.Exists(log + ".env") ? File.ReadAllText(log + ".env") : "",
            File.Exists(log + ".overlay") ? File.ReadAllText(log + ".overlay") : "",
            copy, report);
    }

    /// <summary>Loopback stand-in for the api and the stream, matching the real admin API's own
    /// shape (verified on the dev station): <c>/health</c> answers <see cref="HealthStatus"/>;
    /// <c>POST /api/auth/login</c> with a JSON <c>{"password":...}</c> body matching
    /// <see cref="AdminPassword"/> answers 204 with a <c>genwave-auth</c> session cookie, else
    /// 401; <c>GET /api/settings</c> without that cookie answers 401, with it answers a JSON
    /// array carrying a <c>{"key":"Loudness:TargetLufs","value":"&lt;<see cref="LoudnessTargetLufs"/>&gt;"}</c>
    /// entry (value as a STRING, same as the real DTO); <c>/stream</c> serves
    /// <see cref="StreamWav"/> bytes (a real ffmpeg reads it as a WAV).</summary>
    public sealed class FakeStation : IDisposable
    {
        const string CookieName = "genwave-auth";
        const string CookieValue = "fake-session";

        readonly HttpListener listener = new();
        readonly CancellationTokenSource stop = new();

        public int HealthStatus { get; set; } = 200;
        public double LoudnessTargetLufs { get; set; } = -14;
        public string AdminPassword { get; set; } = "gate-pass";
        public string? StreamWav { get; set; }
        public string? RestartMarker { get; set; }
        public string? StreamAfterRestart { get; set; }
        public string BaseUrl { get; }

        string? CurrentStream =>
            StreamAfterRestart is not null && RestartMarker is not null && File.Exists(RestartMarker)
                ? StreamAfterRestart : StreamWav;

        public FakeStation()
        {
            var port = FreePort();
            BaseUrl = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(BaseUrl);
            listener.Start();
            _ = Task.Run(ServeAsync);
        }

        async Task ServeAsync()
        {
            while (!stop.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch (Exception) when (stop.IsCancellationRequested || !listener.IsListening) { return; }
                _ = Task.Run(() => Answer(ctx));
            }
        }

        void Answer(HttpListenerContext ctx)
        {
            var res = ctx.Response;
            try
            {
                switch (ctx.Request.Url?.AbsolutePath, ctx.Request.HttpMethod)
                {
                    case ("/health", _):
                        res.StatusCode = HealthStatus;
                        Write(res, "{\"status\":\"ok\"}", "application/json");
                        break;
                    case ("/api/auth/login", "POST"):
                        AnswerLogin(ctx);
                        break;
                    case ("/api/settings", "GET"):
                        AnswerSettings(ctx);
                        break;
                    case ("/stream", _) when CurrentStream is not null:
                        var bytes = File.ReadAllBytes(CurrentStream!);
                        res.ContentType = "audio/wav";
                        res.ContentLength64 = bytes.Length;
                        res.OutputStream.Write(bytes);
                        break;
                    default:
                        res.StatusCode = 404;
                        break;
                }
            }
            catch (Exception)
            {
                // The client (curl/ffmpeg under `-t 1`) hangs up early by design.
            }
            finally
            {
                try { res.Close(); } catch (Exception) { }
            }
        }

        /// <summary>Mirrors the real <c>POST /api/auth/login</c>: a JSON <c>{"password":...}</c>
        /// body matching <see cref="AdminPassword"/> gets a session cookie + 204, anything else
        /// (missing/wrong password, unparsable body) gets 401.</summary>
        void AnswerLogin(HttpListenerContext ctx)
        {
            var res = ctx.Response;
            using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
            var body = reader.ReadToEnd();

            string? password = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("password", out var passwordElement))
                    password = passwordElement.GetString();
            }
            catch (JsonException)
            {
                // Falls through to the 401 below — an unparsable body is just a failed login.
            }

            if (password == AdminPassword)
            {
                res.StatusCode = 204;
                res.Headers.Add("Set-Cookie", $"{CookieName}={CookieValue}; Path=/");
            }
            else
            {
                res.StatusCode = 401;
            }
        }

        /// <summary>Mirrors the real <c>GET /api/settings</c>: 401 without the session cookie
        /// <see cref="AnswerLogin"/> hands out, otherwise the settings array carrying
        /// <see cref="LoudnessTargetLufs"/> under the real DTO's key/value shape.</summary>
        void AnswerSettings(HttpListenerContext ctx)
        {
            var res = ctx.Response;
            var cookie = ctx.Request.Headers["Cookie"] ?? "";
            if (!cookie.Contains($"{CookieName}={CookieValue}", StringComparison.Ordinal))
            {
                res.StatusCode = 401;
                return;
            }

            var target = LoudnessTargetLufs.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var body = $$"""[{"key":"Loudness:TargetLufs","value":"{{target}}"},{"key":"Station:Name","value":"Gate"}]""";
            Write(res, body, "application/json");
        }

        static void Write(HttpListenerResponse res, string body, string contentType)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            res.ContentType = contentType;
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes);
        }

        static int FreePort()
        {
            using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            return ((IPEndPoint)socket.LocalEndpoint).Port;
        }

        public void Dispose()
        {
            stop.Cancel();
            try { listener.Stop(); listener.Close(); } catch (Exception) { }
        }
    }

    /// <summary>Synthesises a WAV with the real ffmpeg: a tone shaped to <paramref name="lufs"/>,
    /// optionally with a 3-second digital-silence gap in the middle.</summary>
    public static string MakeWav(double lufs, int seconds = 6, bool withGap = false)
    {
        var path = Path.Combine(TempDir.CreateForProcessLifetime(), "tone.wav");
        var half = seconds / 2;
        var graph = withGap
            ? $"sine=frequency=440:duration={half},loudnorm=I={lufs}:TP=-1:LRA=7[a];anullsrc=r=48000:cl=mono:d=3[s];sine=frequency=440:duration={half},loudnorm=I={lufs}:TP=-1:LRA=7[b];[a][s][b]concat=n=3:v=0:a=1"
            : $"sine=frequency=440:duration={seconds},loudnorm=I={lufs}:TP=-1:LRA=7";
        var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg")
        {
            RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false,
        };
        foreach (var a in new[] { "-nostats", "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", graph, "-ar", "48000", "-ac", "1", path })
            psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed to synthesise {path}: {err}");
        return path;
    }

    public static string RealPath => Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin";
}
