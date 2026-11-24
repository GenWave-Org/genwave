namespace GenWave.Tts.Tests.Fakes;

using System.Runtime.CompilerServices;
using System.Text;

/// <summary>
/// Compares captured prompts to the checked-in files under <c>Golden/</c> byte for byte (F220.21).
/// Setting <c>GENWAVE_UPDATE_GOLDEN=1</c> rewrites the source files instead of comparing; it refuses to run
/// when <c>CI</c> is set, so a pipeline can only ever compare.
/// </summary>
static class GoldenFiles
{
    public const string UpdateVariable = "GENWAVE_UPDATE_GOLDEN";

    static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The names of every file whose captured text differs from (or has no) golden; empty when all match.</summary>
    public static IReadOnlyList<string> Mismatches(IEnumerable<(string File, string Actual)> captured)
    {
        var updating = Environment.GetEnvironmentVariable(UpdateVariable) == "1";
        if (updating && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
            throw new InvalidOperationException($"{UpdateVariable} must never be set on CI.");

        var mismatched = new List<string>();
        foreach (var (file, actual) in captured)
        {
            if (updating)
            {
                File.WriteAllText(Path.Combine(SourceDirectory(), file), actual, Utf8NoBom);
                continue;
            }

            var path = Path.Combine(AppContext.BaseDirectory, "Golden", file);
            if (!File.Exists(path) || File.ReadAllText(path, Utf8NoBom) != actual)
                mismatched.Add(file);
        }

        return mismatched;
    }

    static string SourceDirectory([CallerFilePath] string thisFile = "")
    {
        var fakes = Path.GetDirectoryName(thisFile) ?? throw new InvalidOperationException("No caller path.");
        return Path.GetFullPath(Path.Combine(fakes, "..", "Golden"));
    }
}
