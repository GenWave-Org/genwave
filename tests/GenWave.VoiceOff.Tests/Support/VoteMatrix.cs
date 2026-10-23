using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Score;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>A score <see cref="Matrix"/> made of plain-lane pairs with the challenger on side A, whose (wins, losses) are decided by one judge.</summary>
internal static class VoteMatrix
{
    internal const string Baseline = "quillbase";

    internal static Matrix Of(params (string Engine, int Wins, int Losses)[] engines)
    {
        List<ManifestPair> pairs = [];
        List<Vote> votes = [];
        foreach ((string engine, int wins, int losses) in engines)
        {
            for (int i = 0; i < wins + losses; i++)
            {
                string id = $"{engine}-{i}";
                pairs.Add(new ManifestPair(id, "line-" + i, "short", RenderLane.Plain, id + "-a", id + "-b", new PairTruth(engine, Baseline)));
                votes.Add(new Vote(id, "Dean", i < wins ? VotePick.A : VotePick.B, DateTimeOffset.UnixEpoch));
            }
        }

        return Matrix.Build(new PairManifest("run", "hash", pairs, []), votes, Baseline);
    }
}
