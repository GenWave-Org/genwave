namespace GenWave.VoiceOff.Serving;

/// <summary>What the judge decided about one card: how many times they winced, 1 to 5 stars (null while unrated) and their notes.</summary>
internal sealed record CardJudgement(int Winces, int? Stars = null, string Notes = "");
