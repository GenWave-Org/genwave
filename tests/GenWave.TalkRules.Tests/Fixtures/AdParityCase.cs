using GenWave.Ads;
using GenWave.Core.Abstractions;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>One ad reply, the request it is judged against, and the estimator both sides share.</summary>
internal sealed record AdParityCase(string Reply, AdScriptValidationRequest Request, IPatterDurationEstimator Estimator);
