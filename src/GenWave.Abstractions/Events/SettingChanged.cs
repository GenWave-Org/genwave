namespace GenWave.Core.Events;

/// <summary>
/// An allowlisted station setting was written. Deliberately carries the key only — never the
/// value — so a subscriber can't leak a sensitive setting (e.g. <c>Llm:ApiKey</c>) into a log or
/// audit trail by accident; an interested module reads the current value through its own seam.
/// </summary>
public sealed record SettingChanged(string Key) : StationEvent;
