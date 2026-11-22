namespace GenWave.Core.TalkRules;

/// <summary>
/// One spoken line, cleaned and with sound tags stripped. A DJ line is one line.
/// <para>
/// <paramref name="Speaker"/> is the label the script gave the line, not an id: for a chat it is the role
/// tag the crosstalk parser reads ("HOST" or "NEIGHBOR"); for an ad it is the script's voice tag ("ANNOUNCER",
/// "VOICE1", …); for a DJ line it is the speaker's <see cref="TalkSpeaker.Name"/>. Each kind's snapshot source
/// defines how its labels map to <see cref="TalkSnapshot.Speakers"/>.
/// </para>
/// </summary>
public sealed record TalkLine(string Speaker, string Text);
