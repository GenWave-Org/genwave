namespace GenWave.Core.Events;

/// <summary>What kind of talk a rule flagged (SPEC F220.16): the booth row calls it a line or a chat.</summary>
public enum RuleFlagTalk
{
    /// <summary>One DJ's line of patter.</summary>
    Line,

    /// <summary>A two-DJ chat.</summary>
    Chat,
}
