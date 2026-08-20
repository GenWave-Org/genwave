namespace GenWave.Host.Configuration;

/// <summary>
/// Result of <see cref="IStationSettingsStore.WriteIfVersionMatchesAsync"/> (gh-#486): whether the
/// version-guarded write landed, or lost the race to a concurrent editor.
/// </summary>
public enum SettingsWriteOutcome
{
    /// <summary>The write landed — the row's version has advanced.</summary>
    Written,

    /// <summary>
    /// The row's version had already moved (or, for a first write, a row raced into existence)
    /// since the caller's read — nothing was persisted. The caller surfaces this as a 409.
    /// </summary>
    Conflict,
}
