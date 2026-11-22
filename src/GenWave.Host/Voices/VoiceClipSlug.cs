using System.Text;

namespace GenWave.Host.Voices;

/// <summary>
/// The server-made slug for a clip (SPEC F219.15): the label lowercased, every run of anything but a-z and 0-9 collapsed to one
/// hyphen, trimmed; <c>clip</c> when nothing is left. Matches <c>station.voice_clip</c>'s slug CHECK. No part of a client path can
/// survive this, which is the point.
/// </summary>
internal static class VoiceClipSlug
{
    const string Fallback = "clip";
    const int MaxBaseLength = 48;

    public static string FromLabel(string label)
    {
        var slug = new StringBuilder(label.Length);
        foreach (var c in label.ToLowerInvariant())
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
                slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-')
                slug.Append('-');
        }

        var text = slug.ToString().Trim('-');
        if (text.Length > MaxBaseLength)
            text = text[..MaxBaseLength].TrimEnd('-');

        return text.Length == 0 ? Fallback : text;
    }

    /// <summary>The <paramref name="attempt"/>th candidate for <paramref name="baseSlug"/>: the slug itself, then <c>-2</c>, <c>-3</c>...</summary>
    public static string Candidate(string baseSlug, int attempt) => attempt <= 1 ? baseSlug : $"{baseSlug}-{attempt}";
}
