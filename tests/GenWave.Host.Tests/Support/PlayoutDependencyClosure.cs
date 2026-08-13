using Microsoft.Extensions.DependencyInjection;
using GenWave.Host.Playout;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// The F90.8 DI-closure-walk idiom, shared: breadth-first from <see cref="PlayoutFeederService"/>/
/// <see cref="PlayoutSupervisor"/> — the real on-air tick path — through every constructor parameter
/// type, resolving each one through a caller-supplied REAL, live <see cref="IServiceProvider"/> to
/// discover its actual concrete runtime type, then recursing into THAT type's own constructor. Only
/// types in a <c>GenWave.*</c> namespace are followed further (framework/BCL plumbing — <c>ILogger&lt;T&gt;</c>,
/// <c>IOptions&lt;T&gt;</c>, etc. — are dead ends here by design: nothing render-path-shaped lives
/// there). <c>IEnumerable&lt;T&gt;</c>-shaped parameters fan out through every registered
/// implementation of <c>T</c>, not just one.
///
/// <para>
/// Used by every fact that proves "X is unreachable from the on-air render/playout graph" —
/// <c>Story238_ShelfCannotTouchAir</c> (the community catalog surface) and
/// <c>Story324_RespellOracle</c> (the espeak-ng oracle) both drive the identical walk over their own
/// real composition root. Two byte-identical private copies (one per spec file) preceded this —
/// review round 2 finding F6: the "file-scoped types cannot cross files" justification this suite
/// uses elsewhere does not apply here, since neither copy was ever <c>file</c>-scoped — they were
/// ordinary <see langword="static"/> methods nested in <see langword="public"/> classes, free to be
/// shared like every other type in this <c>Support/</c> folder.
/// </para>
/// </summary>
public static class PlayoutDependencyClosure
{
    public static HashSet<Type> Collect(IServiceProvider services)
    {
        var visited = new HashSet<Type>();
        var queue = new Queue<Type>();
        queue.Enqueue(typeof(PlayoutFeederService));
        queue.Enqueue(typeof(PlayoutSupervisor));

        while (queue.TryDequeue(out var type))
        {
            if (!visited.Add(type)) continue;

            var constructor = type.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault();
            if (constructor is null) continue;

            foreach (var parameter in constructor.GetParameters())
                foreach (var concreteType in ResolveConcreteTypes(services, parameter.ParameterType))
                    if ((concreteType.Namespace ?? "").StartsWith("GenWave.", StringComparison.Ordinal))
                        queue.Enqueue(concreteType);
        }

        return visited;
    }

    static IEnumerable<Type> ResolveConcreteTypes(IServiceProvider services, Type parameterType)
    {
        if (parameterType.IsGenericType)
        {
            var openGeneric = parameterType.GetGenericTypeDefinition();
            if (openGeneric == typeof(IEnumerable<>) || openGeneric == typeof(IReadOnlyList<>)
                || openGeneric == typeof(IReadOnlyCollection<>) || openGeneric == typeof(IList<>))
            {
                var elementType = parameterType.GetGenericArguments()[0];
                return services.GetServices(elementType)
                    .OfType<object>()
                    .Select(instance => instance.GetType());
            }
        }

        var resolved = TryResolve(services, parameterType);
        return [resolved?.GetType() ?? parameterType];
    }

    static object? TryResolve(IServiceProvider services, Type type)
    {
        try { return services.GetService(type); }
        catch { return null; } // an unresolvable/open-generic parameter is a dead end, not a fact failure
    }
}
