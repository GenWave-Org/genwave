using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Score;

/// <summary>
/// The tier's engine x lane cells (ARCHITECTURE "Matrix cell"). Pure: the last vote per (pairId, judge) in file order counts,
/// a pick of the challenger's side is a win, a pick of the baseline's side a loss, a tie a tie. Judges are compared ordinal after trim.
/// </summary>
internal sealed class Matrix
{
    private readonly Dictionary<(string Engine, RenderLane Lane), Cell> cells;

    private Matrix(Dictionary<(string Engine, RenderLane Lane), Cell> cells, IReadOnlyList<string> engines, IReadOnlyList<string> judges)
    {
        this.cells = cells;
        Engines = engines;
        Judges = judges;
    }

    /// <summary>The challengers that have at least one pair, in slug order.</summary>
    internal IReadOnlyList<string> Engines { get; }

    /// <summary>Every judge who voted on a pair of this manifest, in ordinal order.</summary>
    internal IReadOnlyList<string> Judges { get; }

    /// <summary>The engine's cell in a lane; an empty cell when it has no pairs there.</summary>
    internal Cell Find(string engine, RenderLane lane) =>
        cells.TryGetValue((engine, lane), out Cell? cell) ? cell : new Cell(engine, lane, Tally.Empty, new Dictionary<string, Tally>());

    /// <exception cref="InvalidDataException">A pair does not have exactly one side from the baseline engine.</exception>
    internal static Matrix Build(PairManifest manifest, IReadOnlyList<Vote> votes, string baseline)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(votes);
        Dictionary<string, Dictionary<string, VotePick>> lastVotes = EffectiveVotes.Last(votes);
        Dictionary<(string, RenderLane), Counter> total = [];
        Dictionary<(string, RenderLane, string), Counter> byJudge = [];
        HashSet<string> engines = new(StringComparer.Ordinal);
        SortedSet<string> judges = new(StringComparer.Ordinal);

        foreach (ManifestPair pair in manifest.Pairs)
        {
            (string challenger, VotePick challengerSide) = Challenger(pair, baseline);
            engines.Add(challenger);
            if (!lastVotes.TryGetValue(pair.PairId, out Dictionary<string, VotePick>? picks))
            {
                continue;
            }

            foreach ((string judge, VotePick pick) in picks)
            {
                judges.Add(judge);
                Counter.At(total, (challenger, pair.Lane)).Add(pick, challengerSide);
                Counter.At(byJudge, (challenger, pair.Lane, judge)).Add(pick, challengerSide);
            }
        }

        Dictionary<(string Engine, RenderLane Lane), Cell> cells = [];
        foreach (string engine in engines)
        {
            foreach (RenderLane lane in Enum.GetValues<RenderLane>())
            {
                SortedDictionary<string, Tally> perJudge = new(StringComparer.Ordinal);
                foreach (string judge in judges)
                {
                    if (byJudge.TryGetValue((engine, lane, judge), out Counter? own))
                    {
                        perJudge[judge] = own.ToTally();
                    }
                }

                cells[(engine, lane)] = new Cell(engine, lane, total.TryGetValue((engine, lane), out Counter? all) ? all.ToTally() : Tally.Empty, perJudge);
            }
        }

        return new Matrix(cells, engines.Order(StringComparer.Ordinal).ToArray(), judges.ToArray());
    }

    private static (string Engine, VotePick Side) Challenger(ManifestPair pair, string baseline)
    {
        bool aIsBaseline = string.Equals(pair.Truth.A, baseline, StringComparison.Ordinal);
        bool bIsBaseline = string.Equals(pair.Truth.B, baseline, StringComparison.Ordinal);
        if (aIsBaseline == bIsBaseline)
        {
            throw new InvalidDataException($"Pair {pair.PairId} must have exactly one side from the baseline engine '{baseline}'.");
        }

        return aIsBaseline ? (pair.Truth.B, VotePick.B) : (pair.Truth.A, VotePick.A);
    }

    private sealed class Counter
    {
        private int wins;
        private int losses;
        private int ties;

        internal static Counter At<TKey>(Dictionary<TKey, Counter> map, TKey key)
            where TKey : notnull
        {
            if (!map.TryGetValue(key, out Counter? counter))
            {
                counter = new Counter();
                map[key] = counter;
            }

            return counter;
        }

        internal void Add(VotePick pick, VotePick challengerSide)
        {
            if (pick == VotePick.Tie)
            {
                ties++;
            }
            else if (pick == challengerSide)
            {
                wins++;
            }
            else
            {
                losses++;
            }
        }

        internal Tally ToTally() => new(wins, losses, ties);
    }
}
