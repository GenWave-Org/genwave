// find_smoke_candidates.cs
// Scans a folder of .mp3/.flac, measures each track's loudness + true peak via FFmpeg
// ebur128, computes the playout gain (target - measured, clamped by true-peak headroom),
// and picks the most DIVERGENT measurable pair so a crossfade smoke test actually
// demonstrates level matching (a track that needs boosting vs. one that needs cutting).
//
// Run:   dotnet run -- <media-root> [target_lufs] [ceiling_dbtp]
//   e.g. dotnet run -- /srv/radio/media
// Requires: .NET 8+, ffmpeg + ffprobe on PATH (ffmpeg built with libswresample for peak=true).
//
// Emits a human-readable table and writes smoke-candidates.json (consumed by smoke_test.sh):
//   { "target_lufs": -16, "a": {...quiet, +gain}, "b": {...loud, -gain}, "gap_db": 7.3 }

using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

const double GateFloor = -70.0;       // R128 silence gate — never auto-amplify at/below this
const double MinDurationSec = 25.0;   // long enough that the smoke test's analysis windows fit
const double MinGapDb = 4.0;          // below this, the pair barely differs — weak test

var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var target = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : -16.0;
var ceiling = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : -1.0;
// Measuring loudness means decoding whole files, so a full library (often thousands of FLACs) is slow.
// Cap the number measured and sample a strided subset across the catalogue — fast, and still varied
// enough to find a divergent pair. Pass 0 to scan everything.
var maxFiles = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 120;

if (!Directory.Exists(root)) { Console.Error.WriteLine($"No such folder: {root}"); return 1; }

var allFiles = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
    .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
             || f.EndsWith(".flac", StringComparison.OrdinalIgnoreCase))
    .OrderBy(f => f, StringComparer.Ordinal)
    .ToArray();

if (allFiles.Length < 2) { Console.Error.WriteLine($"Need >=2 audio files under {root}; found {allFiles.Length}."); return 1; }

var files = (maxFiles <= 0 || allFiles.Length <= maxFiles)
    ? allFiles
    : Enumerable.Range(0, maxFiles).Select(i => allFiles[(int)((long)i * allFiles.Length / maxFiles)]).ToArray();

Console.Error.WriteLine($"Scanning {files.Length} of {allFiles.Length} files under {root} ...");

var results = new List<Candidate>();
var gate = new object();
await Parallel.ForEachAsync(files,
    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
    async (file, ct) =>
    {
        var c = await Measure(file, root, target, ceiling, ct);
        lock (gate) results.Add(c);
        Console.Error.Write(".");
    });
Console.Error.WriteLine();

// --- Report (sorted by gain: most-positive = quietest source, most-negative = loudest) ---
var measurable = results.Where(r => r.Measurable).OrderByDescending(r => r.GainDb).ToList();
Console.WriteLine($"\n{"GAIN dB",8}  {"LUFS",7}  {"PEAK",6}  {"DUR s",7}  PATH");
foreach (var r in results.OrderByDescending(r => r.Measurable).ThenByDescending(r => r.GainDb))
    Console.WriteLine($"{(r.Measurable ? r.GainDb.ToString("+0.00;-0.00") : "  --  "),8}  " +
                      $"{r.Lufs,7:0.0}  {r.TruePeak,6:0.0}  {r.DurationSec,7:0.0}  {r.MediaPath}" +
                      (r.Measurable ? "" : "   (unmeasurable: silent/short — excluded)"));

// --- Pick the divergent pair from measurable, sufficiently-long tracks that can actually reach the
//     target. A very quiet track whose boost is clamped by true-peak headroom lands BELOW target, so
//     it would (correctly) fail the smoke test's "lands at target" check — exclude those, else the
//     test is picking material it can't satisfy. ---
bool ReachesTarget(Candidate r) => target - r.Lufs <= ceiling - r.TruePeak + 0.05;
var eligible = measurable.Where(r => r.DurationSec >= MinDurationSec && ReachesTarget(r)).ToList();
if (eligible.Count < 2)
{
    Console.Error.WriteLine($"\nNeed >=2 measurable, target-reachable tracks >= {MinDurationSec}s long. Add more material.");
    return 1;
}
var quiet = eligible.First();   // highest (most positive) gain => needs boosting
var loud  = eligible.Last();    // lowest  (most negative) gain => needs cutting
var gap   = quiet.GainDb - loud.GainDb;

Console.WriteLine($"\n=== Recommended smoke-test pair (gain gap {gap:0.0} dB) ===");
Console.WriteLine($"  A (quiet, gain {quiet.GainDb:+0.00}): {quiet.MediaPath}");
Console.WriteLine($"  B (loud,  gain {loud.GainDb:+0.00}): {loud.MediaPath}");
if (gap < MinGapDb)
    Console.WriteLine($"  WARNING: gap < {MinGapDb} dB — the test will be weak. Add more varied material.");

var outPath = Path.Combine(Directory.GetCurrentDirectory(), "smoke-candidates.json");
var json =
    "{\n" +
    $"  \"target_lufs\": {Num(target)},\n" +
    $"  \"ceiling_dbtp\": {Num(ceiling)},\n" +
    $"  \"gap_db\": {Num(Math.Round(gap, 2))},\n" +
    $"  \"a\": {CandidateJson(quiet)},\n" +
    $"  \"b\": {CandidateJson(loud)}\n" +
    "}\n";
await File.WriteAllTextAsync(outPath, json);
Console.WriteLine($"\nWrote {outPath}");
return 0;

// ----------------------------------------------------------------------------------------

async Task<Candidate> Measure(string file, string root, double tgt, double ceil, CancellationToken ct)
{
    var (lufs, tp) = await Ebur128(file, ct);
    var dur = await ProbeDuration(file, ct);
    var measurable = lufs > GateFloor && double.IsFinite(tp);
    var gain = measurable ? Math.Min(tgt - lufs, ceil - tp) : 0.0;
    // media path = path the engine sees: /media/<relative-to-root>, with forward slashes
    var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
    return new Candidate("/media/" + rel, file, Math.Round(lufs, 1), Math.Round(tp, 1),
                         Math.Round(dur, 1), measurable, Math.Round(gain, 2));
}

async Task<(double lufs, double tp)> Ebur128(string file, CancellationToken ct)
{
    var p = Run("ffmpeg", ["-nostats", "-hide_banner", "-i", file,
                           "-filter_complex", "ebur128=peak=true", "-f", "null", "-"]);
    var err = await p.StandardError.ReadToEndAsync(ct);
    await p.WaitForExitAsync(ct);
    var s = err[Math.Max(0, err.LastIndexOf("Summary:", StringComparison.Ordinal))..];
    return (ParseNum(Regex.Match(s, @"I:\s*(-?[\d.]+)\s*LUFS")),
            ParseNum(Regex.Match(s, @"Peak:\s*(-?[\d.]+)\s*dBFS")));
}

async Task<double> ProbeDuration(string file, CancellationToken ct)
{
    var p = Run("ffprobe", ["-v", "error", "-show_entries", "format=duration",
                            "-of", "default=nw=1:nk=1", file]);
    var outp = await p.StandardOutput.ReadToEndAsync(ct);
    await p.WaitForExitAsync(ct);
    return double.TryParse(outp.Trim(), out var d) ? d : 0.0;
}

static Process Run(string exe, string[] argv)
{
    var psi = new ProcessStartInfo(exe)
    { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
    foreach (var a in argv) psi.ArgumentList.Add(a);
    return Process.Start(psi)!;
}

static double ParseNum(Match m) =>
    m.Success && double.TryParse(m.Groups[1].Value, out var v) ? v : double.NegativeInfinity;

// Minimal hand-rolled JSON so the tool runs as a file-based app (`dotnet run x.cs`), which has
// reflection-based System.Text.Json disabled. The shape is fixed and consumed by smoke_test.sh.
static string CandidateJson(Candidate c) =>
    "{" +
    $"\"MediaPath\": {Str(c.MediaPath)}, " +
    $"\"HostPath\": {Str(c.HostPath)}, " +
    $"\"Lufs\": {Num(c.Lufs)}, " +
    $"\"TruePeak\": {Num(c.TruePeak)}, " +
    $"\"DurationSec\": {Num(c.DurationSec)}, " +
    $"\"Measurable\": {(c.Measurable ? "true" : "false")}, " +
    $"\"GainDb\": {Num(c.GainDb)}" +
    "}";

static string Str(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
static string Num(double d) => d.ToString("0.0###", CultureInfo.InvariantCulture);

record Candidate(string MediaPath, string HostPath, double Lufs, double TruePeak,
                 double DurationSec, bool Measurable, double GainDb);
