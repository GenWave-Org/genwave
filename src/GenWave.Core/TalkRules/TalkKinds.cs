namespace GenWave.Core.TalkRules;

/// <summary>The kinds of talk the rules cover. A flag set for "kinds a rule can cover"; a snapshot holds exactly one.</summary>
[Flags]
public enum TalkKinds
{
    None = 0,
    DjLine = 1,
    Chat = 2,
    Ad = 4,
}
