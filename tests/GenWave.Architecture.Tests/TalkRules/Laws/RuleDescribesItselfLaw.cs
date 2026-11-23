using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Laws;

/// <summary>
/// Law R6 (F220.1): a rule describes itself. One method per clause so each can be asserted alone.
/// "States its own numbers" is checked as: every numeric <c>const</c> or <c>static readonly</c> field declared on
/// the rule type (private ones included) appears, spelled in digits, in <c>WhatItDoes</c>.
/// Limits, for rule authors: a <c>double</c>, <c>float</c> or <c>decimal</c> must appear exactly as
/// <c>Convert.ToString(x, CultureInfo.InvariantCulture)</c> gives it (or use an int percent constant instead).
/// Not checked: constants on other types, <c>static readonly TimeSpan</c>, and literals written inline in a method.
/// The match is on whole digit runs, so "1" is not found inside "1.5" or "15".
/// </summary>
public static partial class RuleDescribesItselfLaw
{
    public static IReadOnlyList<string> Violations(IReadOnlyList<ITalkRule> rules) =>
    [
        .. IdViolations(rules),
        .. NameViolations(rules),
        .. WhatItDoesViolations(rules),
        .. NumberViolations(rules),
        .. DefaultCoverViolations(rules),
        .. PartViolations(rules),
    ];

    public static IReadOnlyList<string> IdViolations(IReadOnlyList<ITalkRule> rules)
    {
        var violations = rules
            .Where(rule => !KebabId().IsMatch(rule.Id))
            .Select(rule => $"R6 {rule.GetType().Name}: id \"{rule.Id}\" is not kebab-case.")
            .ToList();
        foreach (var group in rules.GroupBy(rule => rule.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            violations.Add($"R6 id \"{group.Key}\" is shared by {string.Join(" and ", group.Select(rule => rule.GetType().Name))}.");
        }

        return violations;
    }

    public static IReadOnlyList<string> NameViolations(IReadOnlyList<ITalkRule> rules) =>
        Sentences(rules, "Name", rule => rule.Describe().Name);

    public static IReadOnlyList<string> WhatItDoesViolations(IReadOnlyList<ITalkRule> rules) =>
        Sentences(rules, "WhatItDoes", rule => rule.Describe().WhatItDoes);

    public static IReadOnlyList<string> NumberViolations(IReadOnlyList<ITalkRule> rules) =>
        rules
            .SelectMany(rule => OwnNumbers(rule.GetType())
                .Where(number => !StatedIn(rule.Describe().WhatItDoes, number))
                .Select(number => $"R6 {rule.GetType().Name}: WhatItDoes does not state its own number {number}."))
            .ToList();

    public static IReadOnlyList<string> DefaultCoverViolations(IReadOnlyList<ITalkRule> rules) =>
        rules
            .Where(rule => (rule.OnByDefault & ~rule.CanCover) != TalkKinds.None)
            .Select(rule => $"R6 {rule.GetType().Name}: OnByDefault {rule.OnByDefault} is not within CanCover {rule.CanCover}.")
            .ToList();

    public static IReadOnlyList<string> PartViolations(IReadOnlyList<ITalkRule> rules) =>
        rules
            .Where(rule => rule is not IJudgingTalkRule && rule is not IShapingTalkRule)
            .Select(rule => $"R6 {rule.GetType().Name}: implements neither IShapingTalkRule nor IJudgingTalkRule.")
            .ToList();

    private static List<string> Sentences(IReadOnlyList<ITalkRule> rules, string field, Func<ITalkRule, string> text) =>
        rules
            .Where(rule => !IsSentence(text(rule)))
            .Select(rule => $"R6 {rule.GetType().Name}: {field} \"{text(rule)}\" must start with a capital and end with a full stop.")
            .ToList();

    private static bool IsSentence(string text) =>
        text.Length > 1 && char.IsUpper(text[0]) && text[^1] == '.';

    private static IEnumerable<string> OwnNumbers(Type type) =>
        type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(field => field.IsLiteral || field.IsInitOnly)
            .Where(field => field.FieldType == typeof(int) || field.FieldType == typeof(long) || field.FieldType == typeof(double)
                || field.FieldType == typeof(float) || field.FieldType == typeof(decimal))
            .Select(field => Convert.ToString(field.GetValue(null), CultureInfo.InvariantCulture) ?? string.Empty);

    private static bool StatedIn(string text, string number) =>
        Regex.IsMatch(text, $@"(?<![\d.]){Regex.Escape(number)}(?!\.?\d)");

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex KebabId();
}
