using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Tts;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>Runs one crosstalk reply through today's parser and through the rules on a snapshot the real source built.</summary>
internal static class ChatParity
{
    static readonly DateTimeOffset Now = TalkSnapshots.DjLine(string.Empty).LocalNow;
    static readonly string[] KeepTags = ["[laughs]"];

    sealed class FixedPosture : IAudiencePostureProvider
    {
        public AudiencePosture Current => AudiencePosture.Everyone;
    }

    /// <summary>Builds a reply from its HOST and NEIGHBOR lines, alternating from HOST.</summary>
    public static string Reply(params string[] lines) =>
        string.Join('\n', lines.Select((text, index) => $"{(index % 2 == 0 ? "HOST" : "NEIGHBOR")}: {text}"));

    /// <summary>Whether the parser discards each reply for a truth reason, then whether the rule flags it.</summary>
    public static (List<bool> Legacy, List<bool> Rule) Verdicts(IJudgingTalkRule rule, IReadOnlyList<string> replies) =>
        ([.. replies.Select(LegacyRejects)], [.. replies.Select(reply => rule.Judge(SnapshotOf(reply)) is not null)]);

    static bool LegacyRejects(string reply) =>
        CrosstalkScriptParser.Parse(reply, 400, 600, Now, "GenWave", KeepTags)
            is CrosstalkWriteResult.Discarded { Cause: LlmCallCause.TruthGateReject };

    static TalkSnapshot SnapshotOf(string reply)
    {
        // The parser's own line hygiene, so a rejected reply still gives the lines the writer would have held.
        var lines = reply.Split('\n').Select(line => line.Split(':', 2)).Select(parts => new CrosstalkAiredLine(
            parts[0] == "HOST" ? CrosstalkSpeaker.Host : CrosstalkSpeaker.Neighbor,
            LlmCopyWriter.ApplyCopyHygiene(parts[1], KeepTags),
            false));
        var card = new PersonaCard(PersonaCard.CurrentSchemaVersion, "Flip", "", "", [], new VoiceSpec("", "af_nova", 1.0, "en"), 0, [], []);
        var request = new CrosstalkExchangeRequest(card, card, "GenWave", null, null, Now);
        var input = new ChatSnapshotInput(request, 1, new CrosstalkAiredScript([.. lines]), "America/Edmonton", 600, 400, [], []);
        return new ChatSnapshotSource(new FixedPosture()).BuildAsync(input, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
    }
}
