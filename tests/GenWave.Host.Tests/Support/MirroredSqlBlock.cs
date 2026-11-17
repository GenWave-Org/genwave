namespace GenWave.Host.Tests.Support;

/// <summary>
/// Reads the text between a <c>-- begin MARKER</c> and <c>-- end MARKER</c> line of a db script, so a spec can
/// pin that db/06 (fresh installs) and its db/NN upgrade script carry byte-identical SQL.
/// </summary>
public static class MirroredSqlBlock
{
    public static string Read(string path, string marker)
    {
        var lines = File.ReadAllLines(path)
            .SkipWhile(l => !l.Contains($"-- begin {marker}", StringComparison.Ordinal))
            .TakeWhile(l => !l.Contains($"-- end {marker}", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(lines);
        return string.Join('\n', lines);
    }
}
