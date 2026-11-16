namespace GenWave.Memory;

/// <summary>How one <see cref="LoreDrafter"/> run ended (SPEC F218.20).</summary>
public enum LoreDraftStatus
{
    /// <summary>At least one usable line came back.</summary>
    Drafted,

    /// <summary>No language model is configured, so nothing was asked.</summary>
    NotConfigured,

    /// <summary>The model answered but nothing in the reply was a usable new line.</summary>
    NoUsableLines,
}
