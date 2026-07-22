namespace GenWave.Core.Domain;

/// <summary>
/// The direction of an operator taste thumb (SPEC F84.1, STORY-215, PLAN T70): nudges the stamped
/// persona's accrued artist rule by ±0.2, clamped to <c>[-1, 1]</c>.
/// </summary>
public enum TasteThumbDirection
{
    Up,
    Down,
}
