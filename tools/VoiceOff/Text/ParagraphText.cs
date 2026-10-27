namespace GenWave.VoiceOff.Text;

/// <summary>
/// One paragraph as a card's engine will be handed it. <see cref="Expected"/> is what the words law
/// holds <see cref="Text"/> to: the script paragraph with the card's cue replacements in place
/// (best-effort card) or its normalised cue-stripped form (as-shipped card).
/// <see cref="DroppedCues"/> are the cues the card could not voice, in reading order.
/// </summary>
internal sealed record ParagraphText(int Number, string Text, string Expected, IReadOnlyList<string> DroppedCues);
