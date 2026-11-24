using GenWave.Core.Domain;
using GenWave.Memory;
using GenWave.Tts;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>The catchphrase-limits rule for specs that only judge: its shaping collaborators are inert.</summary>
internal static class JudgingOnlyCatchphraseLimits
{
    public static CatchphraseLimitsRule Rule() =>
        new(new RepetitionCheck(), new RecordingQuirkSelector(QuirkSelection.None), new TaglineLedger(), new OneCardSource(null));
}
