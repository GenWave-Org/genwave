namespace GenWave.Host.Api;

/// <summary>One rule as <c>GET /api/talk-rules</c> lists it. <c>Switches</c> and <c>Flags7d</c> are keyed by kind slug over <c>CanCover</c>.</summary>
public sealed record TalkRuleDto(
    string Id,
    string Name,
    string WhatItDoes,
    string Why,
    string Example,
    IReadOnlyList<string> CanCover,
    IReadOnlyDictionary<string, bool> Switches,
    IReadOnlyDictionary<string, int> Flags7d);
