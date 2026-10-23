using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using GenWave.VoiceOff.Listening;

namespace GenWave.VoiceOff.Serving;

/// <summary>
/// <c>session/sessions.json</c> loaded once: which sessions exist, which pairs they hold, and the only clip files that may be served.
/// A clip is served by looking its name up here, never by building a path from the request.
/// </summary>
internal sealed partial class SessionBook
{
    private readonly Dictionary<string, SessionEntry> sessions;
    private readonly Dictionary<string, string> clipFiles;
    private readonly HashSet<string> pairIds;

    private SessionBook(int cap, Dictionary<string, SessionEntry> sessions, Dictionary<string, string> clipFiles, HashSet<string> pairIds)
    {
        Cap = cap;
        this.sessions = sessions;
        this.clipFiles = clipFiles;
        this.pairIds = pairIds;
    }

    private int Cap { get; }

    /// <exception cref="InvalidDataException">The file is missing, malformed, repeats an id, or names a clip that is not a <c>clips/&lt;32 hex&gt;.wav</c> file.</exception>
    internal static SessionBook Load(string sessionDirectory)
    {
        string path = Path.Combine(sessionDirectory, "sessions.json");
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"{sessionDirectory} has no sessions.json.");
        }

        SessionFile file;
        try
        {
            file = StrictJson.Read<SessionFile>(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"sessions.json is not valid: {ex.Message}", ex);
        }

        Dictionary<string, SessionEntry> sessions = new(StringComparer.Ordinal);
        Dictionary<string, string> clipFiles = new(StringComparer.Ordinal);
        HashSet<string> pairIds = new(StringComparer.Ordinal);
        foreach (SessionEntry session in file.Sessions)
        {
            if (!sessions.TryAdd(session.Id, session))
            {
                throw new InvalidDataException($"sessions.json repeats session '{session.Id}'.");
            }

            foreach (SessionPair pair in session.Pairs)
            {
                pairIds.Add(pair.PairId);
                AddClip(clipFiles, sessionDirectory, pair.A);
                AddClip(clipFiles, sessionDirectory, pair.B);
            }
        }

        return new SessionBook(file.Cap, sessions, clipFiles, pairIds);
    }

    internal bool TryGetSession(string? id, [NotNullWhen(true)] out SessionEntry? session)
    {
        session = null;
        return id is not null && sessions.TryGetValue(id, out session);
    }

    internal bool HasPair(string pairId) => pairIds.Contains(pairId);

    /// <summary>The page shows at most this many pairs of a session.</summary>
    internal int CapFor(SessionEntry session) => Math.Min(Cap, session.Pairs.Count);

    /// <summary>The file behind a clip name from the request, or false when no session lists it.</summary>
    internal bool TryGetClipFile(string name, [NotNullWhen(true)] out string? file) =>
        clipFiles.TryGetValue("clips/" + name, out file);

    private static void AddClip(Dictionary<string, string> clipFiles, string sessionDirectory, string clip)
    {
        if (!ClipShape().IsMatch(clip))
        {
            throw new InvalidDataException($"sessions.json names a clip '{clip}' that is not clips/<32 hex>.wav.");
        }

        clipFiles[clip] = Path.Combine(sessionDirectory, "clips", clip["clips/".Length..]);
    }

    [GeneratedRegex(@"\Aclips/[0-9a-f]{32}\.wav\z", RegexOptions.CultureInvariant)]
    private static partial Regex ClipShape();
}
