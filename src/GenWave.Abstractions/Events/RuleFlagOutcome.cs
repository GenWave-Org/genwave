namespace GenWave.Core.Events;

/// <summary>What the writer did about a flagged line (SPEC F220.16).</summary>
public enum RuleFlagOutcome
{
    /// <summary>The writer asked the model for one rewrite.</summary>
    ReAsked,

    /// <summary>The rewrite was flagged again; stock template copy airs instead.</summary>
    Template,

    /// <summary>The rewrite was flagged again and template copy never airs here; the talk is dropped.</summary>
    Dropped,
}
