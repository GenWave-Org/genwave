namespace GenWave.VoiceOff.Score;

/// <summary>Where <c>RESULTS.md</c> goes when <c>--out</c> is not given: <c>tools/VoiceOff/RESULTS.md</c> of the repo the tool was built in, else null.</summary>
internal static class ResultsPath
{
    internal const string FileName = "RESULTS.md";

    internal static string? Default() => Default(AppContext.BaseDirectory);

    internal static string? Default(string baseDirectory)
    {
        for (DirectoryInfo? directory = new(baseDirectory); directory is not null; directory = directory.Parent)
        {
            string project = Path.Combine(directory.FullName, "tools", "VoiceOff", "VoiceOff.csproj");
            if (File.Exists(project))
            {
                return Path.Combine(directory.FullName, "tools", "VoiceOff", FileName);
            }
        }

        return null;
    }
}
