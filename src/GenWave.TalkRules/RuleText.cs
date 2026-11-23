using GenWave.Core.TalkRules;
using GenWave.Tts;

namespace GenWave.TalkRules;

/// <summary>The text the truth checks read: the attempt's lines joined with one space, sound tags removed, as the legacy checks read it. A line that is only a sound tag is dropped, so it leaves no double space. Both snapshot sources already strip sound tags per line, so <c>SoundTags.Strip</c> here only guards snapshots built elsewhere (stored or replayed).</summary>
internal static class RuleText
{
    public static string Of(TalkSnapshot snapshot) =>
        SoundTags.Strip(string.Join(' ', snapshot.Lines.Select(line => line.Text).Where(text => text.Length > 0)));
}
