namespace GenWave.Core.Events;

/// <summary>Which step of a talk rule threw (SPEC F220.9): the booth row says "could not shape" or "could not record".</summary>
public enum RuleBrokenStep
{
    /// <summary>The rule's shaper threw before the model was asked.</summary>
    Shape,

    /// <summary>The rule's recorder threw after the talk was accepted.</summary>
    Record,
}
