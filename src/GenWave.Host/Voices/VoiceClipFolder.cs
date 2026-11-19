using System.Diagnostics;
using GenWave.Tts;
using Microsoft.Extensions.Options;

namespace GenWave.Host.Voices;

/// <summary>
/// The clips folder (<see cref="TtsOptions.ClipsRoot"/>, SPEC F219.12/F219.28): a clip's file is <c>&lt;slug&gt;.wav</c> directly in it,
/// the name the cloning engine lists as the voice. The one place a clip's path is built. Every path that carries a slug is made
/// with <see cref="Path.GetFullPath(string)"/> and checked with <see cref="IsUnderCanonicalRoot"/> in the same method that writes or
/// deletes it (the repo's path-guard law, as <c>VoicePackController</c> does).
/// </summary>
public sealed class VoiceClipFolder(IOptionsMonitor<TtsOptions> ttsOptions)
{
    const string Extension = ".wav";
    const string StarterMarkerName = ".starter-seeded";

    /// <summary>A fresh, unique, not-yet-existing path in the folder to write a clip to before it is published. The name is a GUID, never client text.</summary>
    public string NewStagingPath()
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ttsOptions.CurrentValue.ClipsRoot));
        Directory.CreateDirectory(root);
        return Path.Combine(root, $".{Guid.NewGuid():N}.part");
    }

    /// <summary>
    /// Moves the staged file to <c>&lt;slug&gt;.wav</c> in the folder. Returns false, moving nothing, when that name is already
    /// there (never overwrites).
    /// </summary>
    public bool Publish(string stagingPath, string slug)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ttsOptions.CurrentValue.ClipsRoot));
        var target = Path.GetFullPath(Path.Combine(root, slug + Extension));
        if (!IsUnderCanonicalRoot(target, root))
            throw new UnreachableException("A clip slug resolved outside the clips folder.");

        if (File.Exists(target))
            return false;

        try
        {
            File.Move(stagingPath, target, overwrite: false);
            return true;
        }
        catch (IOException) when (File.Exists(target))
        {
            // Lost a race for the same name between the check and the move.
            return false;
        }
    }

    /// <summary>True when <c>&lt;slug&gt;.wav</c> is already in the folder.</summary>
    public bool Exists(string slug)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ttsOptions.CurrentValue.ClipsRoot));
        var target = Path.GetFullPath(Path.Combine(root, slug + Extension));
        if (!IsUnderCanonicalRoot(target, root))
            throw new UnreachableException("A clip slug resolved outside the clips folder.");

        return File.Exists(target);
    }

    /// <summary>The slugs recorded in the starter marker (SPEC F219.14); empty when there is no marker.</summary>
    public IReadOnlyCollection<string> ReadSeededStarterSlugs()
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ttsOptions.CurrentValue.ClipsRoot));
        var marker = Path.GetFullPath(Path.Combine(root, StarterMarkerName));
        if (!IsUnderCanonicalRoot(marker, root))
            throw new UnreachableException("The starter marker resolved outside the clips folder.");

        return File.Exists(marker)
            ? [.. File.ReadAllLines(marker).Select(l => l.Trim()).Where(l => l.Length > 0)]
            : [];
    }

    /// <summary>Records <paramref name="slug"/> as seeded so a starter the admin later deletes is not seeded again.</summary>
    public void MarkStarterSeeded(string slug)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ttsOptions.CurrentValue.ClipsRoot));
        var marker = Path.GetFullPath(Path.Combine(root, StarterMarkerName));
        if (!IsUnderCanonicalRoot(marker, root))
            throw new UnreachableException("The starter marker resolved outside the clips folder.");

        Directory.CreateDirectory(root);
        // A dotfile: the cloning engine's listing of this folder (T770) must skip names starting with '.', as it does staging files.
        File.AppendAllLines(marker, [slug]);
    }

    /// <summary>Deletes <c>&lt;slug&gt;.wav</c> from the folder; a file already gone is fine.</summary>
    public void Remove(string slug)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ttsOptions.CurrentValue.ClipsRoot));
        var target = Path.GetFullPath(Path.Combine(root, slug + Extension));
        if (!IsUnderCanonicalRoot(target, root))
            throw new UnreachableException("A clip slug resolved outside the clips folder.");

        File.Delete(target);
    }

    /// <summary>Moves a just-published <c>&lt;slug&gt;.wav</c> back to <paramref name="stagingPath"/> so the upload can try another slug.
    /// A delete of the drifted row plus a third upload of the same slug landing between our publish and this call could
    /// move that upload's file instead; that admin-only triple race is accepted (the usual outcome is a 500, no leak).</summary>
    public void Unpublish(string slug, string stagingPath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ttsOptions.CurrentValue.ClipsRoot));
        var target = Path.GetFullPath(Path.Combine(root, slug + Extension));
        if (!IsUnderCanonicalRoot(target, root))
            throw new UnreachableException("A clip slug resolved outside the clips folder.");

        File.Move(target, stagingPath, overwrite: true);
    }

    /// <summary>The file name a clip with <paramref name="slug"/> is stored under (relative, as <c>station.voice_clip.file_name</c> holds it).</summary>
    public static string FileNameFor(string slug) => slug + Extension;

    /// <summary>Separator-aware canonical-root check (a bare <c>StartsWith(root)</c> would admit a sibling like <c>/clips-evil</c>).</summary>
    internal static bool IsUnderCanonicalRoot(string candidate, string canonicalRoot) =>
        candidate.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}
