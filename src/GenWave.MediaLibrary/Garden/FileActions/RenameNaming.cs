using System.Text;
using System.Text.RegularExpressions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Garden.FileActions;

/// <summary>
/// Rename-name rules for <see cref="FileActionPlanner"/> (SPEC F154.1, F154.3; STORY-379; PLAN T379,
/// gh-#529), split out of the planner itself for cohesion — everything here is about producing or
/// validating a bare file NAME, never a full path, and never touches the filesystem.
/// </summary>
static partial class RenameNaming
{
    /// <summary>Longest a file name may be, in UTF-8 bytes — the common ext4/most-Linux-filesystem
    /// <c>NAME_MAX</c>.</summary>
    const int MaxNameBytes = 255;

    /// <summary>The template's own artist/title separator — named so <see cref="BuildTemplateName"/>
    /// and its own truncation fallback compute the identical byte budget.</summary>
    const string Separator = " - ";

    /// <summary>
    /// True when <paramref name="name"/> is safe to use as a rename target as-is: non-empty, does not
    /// start with <c>.</c> (T379 review round 2 item 1 — this alone also covers a bare <c>.</c>/<c>..</c>;
    /// see this method's own remarks), no directory separator or any control character (T379 review
    /// B5 — a bare NUL check alone let a literal line feed or ESC through), and no more than
    /// <see cref="MaxNameBytes"/> UTF-8 bytes (SPEC F154.3's <c>InvalidName</c> rule). Callers check
    /// this only AFTER the traversal check (a literal <c>..</c> segment is refused as
    /// <see cref="FileActionRule.Traversal"/> first, per the planner's own ordered rule list) — this
    /// method still rejects a bare <c>..</c> defensively (it starts with <c>.</c> too), since a
    /// caller invoking it standalone (e.g. a future test) should never see it accepted.
    ///
    /// <para>
    /// <b>Leading dot ⇒ Hidden on this deploy target (T379 review round 2 item 1):</b> .NET's
    /// <c>EnumerationOptions</c> default (<c>AttributesToSkip</c>) excludes
    /// <see cref="FileAttributes.Hidden"/> entries, and on Unix a file whose name starts with
    /// <c>.</c> IS Hidden — a rename to <c>.hidden.mp3</c> (or even bare <c>.mp3</c>) would vanish
    /// from the very next scan tick, breaking F154.6's own "the next scan classifies the row
    /// unchanged" contract outright.
    /// </para>
    ///
    /// <para>
    /// <b>Backslash is deliberately still legal here</b> (T379 review N9c): <c>\</c> is not a path
    /// separator on this Linux deploy target — only <see cref="SanitizeForFileName"/> (the TEMPLATE's
    /// own generator) scrubs it defensively, for a tidier generated name; an operator-supplied name
    /// is trusted to mean exactly the literal bytes it names, the same way any other ordinary
    /// character is.
    /// </para>
    /// </summary>
    public static bool IsValidRenameName(string name) =>
        name.Length > 0
        && !name.StartsWith('.')
        && !name.Contains(Path.DirectorySeparatorChar)
        && !name.Contains(Path.AltDirectorySeparatorChar)
        && !name.Any(char.IsControl)
        && Encoding.UTF8.GetByteCount(name) <= MaxNameBytes;

    /// <summary>
    /// True when <paramref name="name"/>'s own extension matches <paramref name="sourcePath"/>'s
    /// (case-insensitive — SPEC F154.1: the container format is never renamed away, T379 review N9a).
    /// A rename can rename the NAME, never the container. An extension-less source only accepts an
    /// extension-less name back — adding one is refused, the same as changing one (T379 review round
    /// 2 item 5). A case-only difference (<c>Song.MP3</c> against a source's <c>.mp3</c>) is accepted
    /// — the comparison is ordinal-ignore-case — even though <see cref="BuildTemplateName"/>'s own
    /// generated extension is always lower-cased (T379 review round 2 item 5): the template picks ONE
    /// casing convention for names it generates itself; an operator is free to pick their own.
    /// </summary>
    public static bool HasSourceExtension(string name, string sourcePath) =>
        string.Equals(Path.GetExtension(name), Path.GetExtension(sourcePath), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The <c>{artist} - {title}.{ext}</c> template (F154.1) — catalog artist/title, sanitised for a
    /// file name, falling back to <c>Unknown Artist</c>/<c>Unknown Title</c>; the source file's own
    /// extension, lower-cased. If the assembled name would still fail <see cref="IsValidRenameName"/>
    /// on length alone (a very long artist/title pair — T379 review B6, a 400-character artist would
    /// otherwise mint a plan T380 could never execute, ENAMETOOLONG), the artist and title are each
    /// truncated on a UTF-8 BYTE boundary — never mid-character — to fit the template inside
    /// <see cref="MaxNameBytes"/> alongside the separator and extension.
    ///
    /// <para>
    /// <b>This method does NOT itself guarantee the result passes <see cref="IsValidRenameName"/>
    /// (T379 review round 2 item 2)</b> — the truncation above is arithmetic, not a proof: a source
    /// file whose OWN extension alone is already within a few bytes of <see cref="MaxNameBytes"/>
    /// can still overflow even once both halves have been truncated all the way down to nothing. The
    /// caller (<c>FileActionPlanner.PlanRename</c>) re-validates the returned name as an enforced
    /// postcondition and refuses <see cref="FileActionRule.InvalidName"/> rather than ever planning a
    /// name this method could not actually make valid.
    /// </para>
    /// </summary>
    public static string BuildTemplateName(FileActionSubject subject)
    {
        var artist = SanitizeForFileName(subject.Artist, "Unknown Artist");
        var title = SanitizeForFileName(subject.Title, "Unknown Title");
        var extension = Path.GetExtension(subject.Path).ToLowerInvariant();

        var name = $"{artist}{Separator}{title}{extension}";
        if (IsValidRenameName(name)) return name;

        var budget = Math.Max(0, MaxNameBytes - Encoding.UTF8.GetByteCount(Separator) - Encoding.UTF8.GetByteCount(extension));
        var half = budget / 2;
        var truncatedArtist = TruncateUtf8(artist, half);
        var truncatedTitle = TruncateUtf8(title, budget - half);

        return $"{truncatedArtist}{Separator}{truncatedTitle}{extension}";
    }

    /// <summary>
    /// Truncates <paramref name="value"/> to at most <paramref name="maxBytes"/> UTF-8 bytes,
    /// backing off from the cut point while it lands on a UTF-8 CONTINUATION byte (the top two bits
    /// are <c>10</c>) so a multi-byte character is never split — the split half would decode as the
    /// U+FFFD replacement character instead of disappearing cleanly.
    /// </summary>
    static string TruncateUtf8(string value, int maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= maxBytes) return value;

        var cut = maxBytes;
        while (cut > 0 && (bytes[cut] & 0xC0) == 0x80) cut--;

        return Encoding.UTF8.GetString(bytes, 0, cut);
    }

    /// <summary>
    /// Sanitises <paramref name="value"/> for use inside a file name: separators, NUL, and control
    /// characters become <c>_</c>; runs of whitespace collapse to a single space; leading/trailing
    /// whitespace is trimmed. <paramref name="fallback"/> covers a null/blank input and the (rare)
    /// case sanitisation empties the string entirely.
    /// </summary>
    static string SanitizeForFileName(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
            builder.Append(IsForbidden(ch) ? '_' : ch);

        var collapsed = WhitespaceRun().Replace(builder.ToString(), " ").Trim();
        return collapsed.Length == 0 ? fallback : collapsed;
    }

    static bool IsForbidden(char ch) =>
        ch is '/' or '\\' or '\0' || char.IsControl(ch);

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
