namespace GenWave.Host.Options;

/// <summary>
/// The DJ life extractor's live switch (SPEC F218.17, STORY-537, PLAN T704a). Bound to <c>Memory:LifeExtraction</c>;
/// joins the settings allowlist as <c>Memory:LifeExtraction:Enabled</c>.
/// </summary>
public sealed class MemoryLifeExtractionOptions
{
    public const string Section = "Memory:LifeExtraction";

    /// <summary>On by default so a fresh install remembers the life its DJs invent; off stops every extraction model call.</summary>
    public bool Enabled { get; set; } = true;
}
