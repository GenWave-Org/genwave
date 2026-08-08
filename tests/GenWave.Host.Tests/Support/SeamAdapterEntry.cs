using Microsoft.Extensions.DependencyInjection;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// One <see cref="ServiceDescriptor"/> registered against a <see cref="SeamPort"/> — T216's raw
/// unit of "who answers this port." <see cref="AdapterType"/> is the concrete CLR type the
/// registration actually produces, resolved by <see cref="SeamCompositionSnapshot"/> whether the
/// descriptor carries an <c>ImplementationType</c>, a pre-built instance, or a factory delegate
/// (<c>services.AddSingleton&lt;IPort&gt;(sp =&gt; sp.GetRequiredService&lt;Concrete&gt;())</c> is
/// common, but far from the only shape this codebase uses). <see cref="IsEffective"/> marks the LAST
/// registration for its port in composition-root order — the one <c>IServiceProvider.GetService</c>
/// actually returns; every earlier entry for the same port is labeled "also registered" in SEAMS.md
/// (never "shadowed"/"overridden" — a multiply-registered port may be a `TryAdd`-default later
/// replaced, or one leg of an `IEnumerable&lt;T&gt;` fan-out where every entry stays active; nothing
/// on a <see cref="ServiceDescriptor"/> distinguishes the two, so <c>SeamIndexDocument</c> (in
/// <c>tools/SeamIndexGenerator</c>) states the raw fact instead of guessing intent). This type is
/// unrelated to decorator-chain derivation (<c>DecoratorChain</c>, also in
/// <c>tools/SeamIndexGenerator</c>) — that walks a SINGLE adapter's own constructor, not the list of
/// registrations for its port.
/// </summary>
public sealed record SeamAdapterEntry(Type AdapterType, ServiceLifetime Lifetime, bool IsEffective);
