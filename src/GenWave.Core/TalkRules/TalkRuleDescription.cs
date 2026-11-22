namespace GenWave.Core.TalkRules;

/// <summary>How a rule explains itself to people; <paramref name="WhatItDoes"/> states its own numbers.</summary>
public sealed record TalkRuleDescription(string Name, string WhatItDoes, string Why, string Example);
