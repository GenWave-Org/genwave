namespace GenWave.Core.Abstractions;

/// <summary>
/// The operator's live off-switch for life extraction (<c>Memory:LifeExtraction:Enabled</c>). Read per copy, so a
/// change applies to the very next accepted copy with no restart. Off means the extractor never calls the model.
/// </summary>
public interface ILifeExtractionSwitch
{
    /// <summary>Whether accepted copies are read for life claims right now.</summary>
    bool IsEnabled { get; }
}
