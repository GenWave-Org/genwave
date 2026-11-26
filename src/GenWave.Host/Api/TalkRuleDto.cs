namespace GenWave.Host.Api;

/// <summary>One rule as <c>GET /api/talk-rules</c> lists it. <c>Switches</c>, <c>Flags7d</c> and <c>Broken7d</c> (shaper or recorder that threw, F220.9) are keyed by kind slug over <c>CanCover</c>.</summary>
public sealed record TalkRuleDto(
    string Id,
    string Name,
    string WhatItDoes,
    string Why,
    string Example,
    IReadOnlyList<string> CanCover,
    IReadOnlyDictionary<string, bool> Switches,
    IReadOnlyDictionary<string, int> Flags7d,
    IReadOnlyDictionary<string, int> Broken7d);
