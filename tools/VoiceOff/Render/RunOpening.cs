namespace GenWave.VoiceOff.Render;

/// <summary>
/// Opens the run folder for a render (SPEC F217.5): a whole run starts a fresh folder, and refuses to overwrite one that
/// already has a <c>run.json</c>; <c>--card</c> joins the existing run, or starts one holding just that card.
/// </summary>
internal static class RunOpening
{
    /// <returns>The manifest to add cards to, or null with <paramref name="problem"/> saying why the run cannot start.</returns>
    internal static async Task<(RunManifest? Manifest, string? Problem)> OpenAsync(
        RunLayout layout, string scriptHash, bool singleCard, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(layout);
        bool exists = File.Exists(layout.RunJsonPath);
        if (exists && !singleCard)
        {
            return (null, $"The run {layout.RunId} already exists in {layout.RunDirectory}; use --card <id> to rewrite one card, or pick a new run id.");
        }

        if (exists)
        {
            RunManifest existing;
            try
            {
                existing = await RunManifest.ReadAsync(layout.RunJsonPath, ct);
            }
            catch (InvalidDataException ex)
            {
                return (null, ex.Message);
            }

            return string.Equals(existing.ScriptHash, scriptHash, StringComparison.Ordinal)
                ? (existing, null)
                : (null, $"The run {layout.RunId} was rendered from a different script (hash {existing.ScriptHash}, now {scriptHash}); render the whole run again under a new id.");
        }

        return await StartAsync(layout, new RunManifest(layout.RunId, scriptHash, now, new Dictionary<string, CardEntry>()), singleCard, ct);
    }

    private static async Task<(RunManifest? Manifest, string? Problem)> StartAsync(RunLayout layout, RunManifest manifest, bool singleCard, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(layout.RunDirectory);
            if (!singleCard)
            {
                File.Delete(layout.RendersPath);
            }

            await manifest.WriteAsync(layout.RunJsonPath, ct);
            return (manifest, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"Cannot create the run folder {layout.RunDirectory}: {ex.Message}");
        }
    }
}
