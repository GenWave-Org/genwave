namespace GenWave.PatterBench;

/// <summary>Writes the finished report in one move, so a reader never sees half of it.</summary>
internal static class ReportFile
{
    internal static void WriteOnce(string path, string content)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
