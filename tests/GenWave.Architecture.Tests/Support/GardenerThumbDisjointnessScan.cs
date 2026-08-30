using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// F155.3's detector (SPEC F150.1, F155.3; STORY-380 AC4; PLAN T367) — a bounded call-graph
/// reachability BFS, seeded at two named ACTION METHODS (never their whole controller class), proving
/// no path from either reaches <c>MediaRatingRepository</c> or <c>PersonaTasteAccrualRepository</c>.
///
/// <b>Why a fresh BFS, not <see cref="MemberCallSiteScan"/>/<see cref="HttpClientMetadataScan"/>.</b>
/// Both existing IL scans answer "does ANY type ANYWHERE reference one specific forbidden member" —
/// an exhaustive scan over every <c>TypeDef</c> in an assembly, never a *reachability* question from a
/// SPECIFIC starting method. This law is the opposite shape: "starting from exactly these two methods,
/// what can they reach, transitively, THROUGH the DI container's own interface→adapter wiring" — e.g.
/// <c>BoothLogController.ThumbStation</c> calls <c>IThumbStore.RecordAsync</c>, which the composition
/// root resolves to <c>MediaThumbRepository.RecordAsync</c>; if THAT method ever called
/// <c>IPersonaTasteAccrualStore.ThumbAsync</c> (a hypothetical, current-code-never-does-this bug), this
/// scan must catch it just as surely as a direct call from the controller itself would. Neither
/// existing scan crosses an interface into its concrete adapter at all — they report "references
/// interface X", never "and X resolves to Y, which does Z".
///
/// <b>Plain SRM over the compiled assemblies (<see cref="IlOperandTable"/>'s own opcode table), not
/// ArchUnitNET or reflection — the SAME reason <see cref="HttpClientMetadataScan"/>'s own remarks give
/// in depth.</b> Both action methods here are <c>async Task&lt;IActionResult&gt;</c>: the C# compiler
/// puts their REAL body inside a compiler-generated state machine, nested somewhere under the
/// declaring type — the declared method's own IL body is boilerplate (construct the state machine,
/// call <c>Start</c>). <see cref="ResolveEffectiveBody"/> redirects every dequeued method to its own
/// state machine's <c>MoveNext</c> BEFORE walking its body — applied uniformly at every BFS step, not
/// only at the two seeds, since <c>MediaThumbRepository.RecordAsync</c> (and most of this reachable
/// subgraph) is itself async. Skipping this redirect would make the whole pin vacuously green: it
/// would see only each method's own state-machine plumbing, never a single real call — exactly the
/// "ArchUnitNET's own type graph never surfaces a closure-within-state-machine" gap
/// <see cref="HostNamespaceTripwire"/>'s remarks name for a different law.
///
/// <b>The redirect is via <c>[AsyncStateMachine]</c>/<c>[IteratorStateMachine]</c>, never a
/// <c>&lt;Name&gt;d__N</c> name-prefix guess (T367 review HIGH-1/MED-1, reproduced: a probe
/// <c>await Task.Run(async () =&gt; await accrual.ThumbAsync(...))</c> inside the action passed the
/// OLD prefix-matching pin outright).</b> A name-prefix search over
/// <c>declaringType.GetNestedTypes()</c> only ever matches Roslyn's plain-named-method state-machine
/// shape (<c>&lt;MethodName&gt;d__N</c>, nested directly under the METHOD's own declaring type) — it
/// is blind to an async LAMBDA's state machine (nested under a synthesized <c>&lt;&gt;c</c> or
/// <c>&lt;&gt;c__DisplayClassN_M</c> closure type, itself nested under the declaring type, named
/// <c>&lt;&lt;Method&gt;b__0_0&gt;d</c> — no <c>__N</c> suffix on the outer angle brackets) and an
/// async LOCAL FUNCTION's (named <c>&lt;&lt;Method&gt;g__Local|1_0&gt;d</c>, same shape). Both are
/// real, reachable call targets today: my BFS already enqueues a lambda's own compiler-generated
/// method correctly (its construction is an ordinary <c>ldftn</c>/delegate <c>newobj</c> — an
/// <c>InlineMethod</c> token like any other call site) — the bug was ONLY in resolving THAT method's
/// own effective body once dequeued. <see cref="ResolveEffectiveBody"/> instead reads the
/// <c>System.Runtime.CompilerServices.AsyncStateMachineAttribute</c> (or, for a hypothetical
/// non-async iterator, <c>IteratorStateMachineAttribute</c>) the compiler stamps directly on EVERY
/// state-machine-backed method — named method, lambda, or local function alike, no naming-convention
/// guess involved — whose single constructor argument names the exact state machine type by its own
/// compiler-emitted name (<c>Outer+Nested+...</c>, walked via <see cref="ResolveTypeByCompilerName"/>,
/// which also closes MED-1's collision below since it resolves from the SPECIFIC dequeued
/// <see cref="MethodDefinitionHandle"/>'s own attribute, never by re-deriving a name from scratch).
///
/// <b>MED-1 — overload collision, closed by the same fix.</b> The old prefix search matched the FIRST
/// nested type starting with <c>&lt;Name&gt;d__</c>, which for two async overloads sharing a name
/// (<c>Helper()</c> and <c>Helper(int)</c>, each with its OWN <c>&lt;Helper&gt;d__N</c> state machine)
/// could silently resolve the WRONG overload's state machine — a call site's own IL token is always
/// unambiguous (it names one specific overload's <see cref="MethodDefinitionHandle"/> directly), so
/// the bug was purely in turning THAT handle into its state machine by name-guessing afterward. Reading
/// the attribute directly off the exact dequeued handle has no name to collide on.
///
/// <b>Interface → adapter resolution — and MED-2's fail-closed rule.</b> A call through a GenWave
/// interface (e.g. <c>IThumbStore</c>) is resolved to its DI-registered EFFECTIVE adapter concrete
/// type via <paramref name="interfaceAdapters"/> (built by the caller from the REAL composition root,
/// <c>SeamCompositionSnapshot.Capture</c> — SEAMS.md's own generator, never a hand-typed re-statement
/// that could drift) before the BFS continues into the adapter's own method body. An interface this
/// scan's own type index recognizes as a GenWave production type, but which
/// <paramref name="interfaceAdapters"/> carries NO entry for (or whose named adapter cannot itself be
/// found), is NOT a silent dead end (T367 review MED-2): <see cref="ResolveCallTargets"/> reports a
/// <see cref="LawViolation"/> naming the unresolved port instead of truncating the walk there — a
/// factory-opaque or unregistered seam is exactly the shape a real bypass could hide behind, so the
/// scan reds rather than vouching for a path it cannot actually see past.
///
/// <b>Boundary — where this scan cannot see a real bypass (named explicitly, not comfort-claimed away —
/// the <see cref="MemberCallSiteScan"/> precedent for stating these plainly).</b>
/// <list type="number">
/// <item><description><b>A closed generic GenWave type or method.</b> A <c>MemberReference</c> whose
/// parent is a <c>TypeSpecification</c> (e.g. a hypothetical generic GenWave interface called with a
/// closed type argument), or a call target reached only through nested generic instantiation, is a dead
/// end here — no production interface in this reachable subgraph is generic today
/// (<c>IThumbStore</c>/<c>IBoothLogReader</c>/<c>IMediaLibraryMembership</c>/<c>ISafeScopeProvider</c>
/// all take/return closed, non-generic shapes), so the gap is real but currently unreachable, not a live
/// hole. A <c>MethodSpecification</c> (a generic METHOD call, e.g. Dapper's own generic query methods)
/// IS unwrapped to its underlying <c>MethodDefOrRef</c> before this check, so this gap is narrower than
/// it first looks — it only bites a closed generic TYPE reference. Unlike an unresolved INTERFACE port
/// (MED-2, above), this narrower generic-type gap is still a silent dead end, not a violation — closing
/// it fully would need a <c>TypeSpecification</c> blob decoder this scan does not carry.</description></item>
/// <item><description><b>Concrete-type dispatch bypassing the interface entirely.</b> A caller typed to
/// a CONCRETE adapter class instead of its port interface reaches that class's own <c>MethodDef</c>
/// directly — this scan follows same-assembly <c>MethodDef</c> call tokens unconditionally regardless
/// of whether the caller went through an interface, so this is not actually a gap for a same-assembly
/// call; it only matters for a HYPOTHETICAL forbidden repository living in a DIFFERENT assembly than its
/// caller while being referenced by concrete type rather than by its interface — not this codebase's
/// shape today (every cross-assembly boundary here is interface-typed).</description></item>
/// <item><description><b>Reflection or a function pointer.</b> Inherited from every SRM-based scan in
/// this suite (<see cref="IlTokenWalker"/>'s own remarks): <c>Type.GetMethod(...).Invoke(...)</c> and
/// <c>calli</c> are both invisible to a static IL-token walk.</description></item>
/// </list>
/// </summary>
internal static class GardenerThumbDisjointnessScan
{
    /// <summary>The two thumb-surface action methods this law fences (SPEC F155.3, STORY-380 AC4) —
    /// the ONE copy the real production fact and any future probe share, mirroring
    /// <see cref="AnnounceSchemeFence.DesignatedCarriers"/>'s own "one copy" precedent. Deliberately
    /// names the ACTION METHOD, not the whole controller: <c>BoothLogController</c> also hosts
    /// <c>ThumbTaste</c>, which legitimately calls <c>IPersonaTasteAccrualStore</c> — fencing the class
    /// would either miss the real question or false-positive on that legitimate sibling.</summary>
    public static readonly IReadOnlyList<(string TypeFullName, string MethodName)> Roots =
    [
        ("GenWave.Host.Api.SpectatorThumbsController", "PostThumb"),
        ("GenWave.Host.Api.BoothLogController", "ThumbStation"),
    ];

    /// <summary>The two repositories neither root may ever reach (SPEC F150.1, F155.3).</summary>
    public static readonly IReadOnlyList<string> ForbiddenTypeFullNames =
    [
        "GenWave.MediaLibrary.Catalog.MediaRatingRepository",
        "GenWave.MediaLibrary.Station.PersonaTasteAccrualRepository",
    ];

    /// <summary>The literal law id this pin reports under — a plain string, deliberately not a
    /// <see cref="LawId"/> const: this pin sits outside the numbered L-table (ARCHITECTURE.md
    /// "Architecture governance" lists it as its own named fitness pin, not an L-row), the same
    /// "labels a LawViolation.LawId string this test itself reads back; it never needs to resolve to a
    /// real law" reasoning <c>Story323_FitnessLawsHoldTheSeamsShut.cs</c>'s own <c>"L7L8Probe"</c>
    /// literal documents for a fixture-only probe — here it is the REAL production fact's own id
    /// instead, but the same non-membership in <see cref="LawId.All"/> applies, so
    /// <see cref="LawParity"/>'s suite↔doc comparison is untouched by this law's own existence.</summary>
    public const string DisjointnessLawId = "F155.3";

    /// <summary>Walks the real call graph from every <paramref name="roots"/> entry, across the DI
    /// container's own interface→adapter wiring (<paramref name="interfaceAdapters"/>), reporting one
    /// <see cref="LawViolation"/> per distinct reachable method declared directly on a
    /// <paramref name="forbiddenTypeFullNames"/> entry — PLUS one per distinct GenWave interface this
    /// walk reaches with no resolvable adapter (MED-2, class remarks): an opaque seam reds rather than
    /// silently truncating.</summary>
    public static IReadOnlyList<LawViolation> FindViolations(
        IReadOnlyList<Assembly> productionAssemblies,
        IReadOnlyDictionary<string, string> interfaceAdapters,
        IReadOnlyList<(string TypeFullName, string MethodName)> roots,
        IReadOnlyList<string> forbiddenTypeFullNames)
    {
        using var universe = new AssemblyUniverse(productionAssemblies);
        var forbidden = new HashSet<string>(forbiddenTypeFullNames, StringComparer.Ordinal);

        var visited = new HashSet<(int AssemblyIndex, int Token)>();
        var queue = new Queue<(int AssemblyIndex, MethodDefinitionHandle Handle)>();
        var violations = new List<LawViolation>();

        void Enqueue(int assemblyIndex, MethodDefinitionHandle handle)
        {
            if (visited.Add((assemblyIndex, MetadataTokens.GetToken(handle))))
                queue.Enqueue((assemblyIndex, handle));
        }

        foreach (var (typeFullName, methodName) in roots)
        {
            var (assemblyIndex, handle) = universe.FindDeclaredMethod(typeFullName, methodName);
            Enqueue(assemblyIndex, handle);
        }

        while (queue.Count > 0)
        {
            var (assemblyIndex, handle) = queue.Dequeue();
            var reader = universe.Readers[assemblyIndex];
            var method = reader.GetMethodDefinition(handle);
            var declaringType = method.GetDeclaringType();

            if (!declaringType.IsNil)
            {
                var declaringTypeFullName = AssemblyUniverse.FullName(reader, declaringType);
                if (forbidden.Contains(declaringTypeFullName))
                {
                    violations.Add(new LawViolation(
                        DisjointnessLawId,
                        declaringTypeFullName,
                        $"reachable via {reader.GetString(method.Name)}() from the station-thumb/listener-thumb " +
                        "action methods (SPEC F150.1, F155.3) — a thumb write path must never reach this type"));
                }
            }

            var bodyHandle = universe.ResolveEffectiveBody(assemblyIndex, handle);
            var bodyMethod = reader.GetMethodDefinition(bodyHandle);
            if (bodyMethod.RelativeVirtualAddress == 0)
                continue; // abstract/extern/interface — no body to walk further.

            var body = universe.PeReaders[assemblyIndex].GetMethodBody(bodyMethod.RelativeVirtualAddress);
            foreach (var token in ReadMethodCallTargets(body.GetILReader()))
            {
                foreach (var target in universe.ResolveCallTargets(assemblyIndex, token, interfaceAdapters, violations))
                    Enqueue(target.AssemblyIndex, target.Handle);
            }
        }

        return violations;
    }

    /// <summary>Every <c>InlineMethod</c>-operand instruction's raw token — <c>call</c>,
    /// <c>callvirt</c>, <c>newobj</c>, <c>ldftn</c>, <c>ldvirtftn</c>, <c>jmp</c> — in encounter order.
    /// Deliberately narrower than <see cref="IlTokenWalker.ResolveTokens{T}"/> (which also yields
    /// <c>InlineField</c>/<c>InlineType</c>/<c>InlineTok</c> tokens for its OWN "any reference at all"
    /// laws): a call-GRAPH edge is specifically an invocation, so a field access or a bare
    /// <c>ldtoken</c>/<c>castclass</c> is read past (its operand bytes still consumed, to keep the
    /// instruction stream aligned) rather than treated as a reachability edge.</summary>
    static IEnumerable<EntityHandle> ReadMethodCallTargets(BlobReader il)
    {
        while (il.RemainingBytes > 0)
        {
            var opByte = il.ReadByte();
            var opCode = opByte == 0xFE ? (short)(0xFE00 | il.ReadByte()) : opByte;

            if (!IlOperandTable.OperandTypeByOpCode.TryGetValue(opCode, out var operandType))
            {
                throw new InvalidOperationException(
                    $"Unrecognized IL opcode 0x{opCode:X4} — the operand table (built from " +
                    "System.Reflection.Emit.OpCodes) is out of date for this runtime.");
            }

            switch (operandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                    il.ReadByte();
                    break;
                case OperandType.InlineVar:
                    il.ReadInt16();
                    break;
                case OperandType.InlineBrTarget or OperandType.InlineI or OperandType.ShortInlineR
                    or OperandType.InlineString or OperandType.InlineSig:
                    il.ReadInt32();
                    break;
                case OperandType.InlineI8 or OperandType.InlineR:
                    il.ReadInt64();
                    break;
                case OperandType.InlineSwitch:
                    var targetCount = il.ReadInt32();
                    for (var i = 0; i < targetCount; i++) il.ReadInt32();
                    break;
                case OperandType.InlineMethod:
                    yield return MetadataTokens.EntityHandle(il.ReadInt32());
                    break;
                case OperandType.InlineField or OperandType.InlineTok or OperandType.InlineType:
                    il.ReadInt32(); // A field/cast/ldtoken target — never a call-graph edge (class remarks).
                    break;
                default:
                    throw new InvalidOperationException($"Unhandled IL operand type {operandType}.");
            }
        }
    }

    /// <summary>Every production assembly's compiled metadata, opened once and held for the whole BFS
    /// (unlike every other SRM scan in this suite, which opens and disposes one assembly at a time —
    /// this one needs several open SIMULTANEOUSLY to cross an assembly boundary mid-walk), plus the
    /// top-level (never nested — class remarks explain why nested types are excluded from the INDEX;
    /// <see cref="ResolveTypeByCompilerName"/> still reaches any nesting depth on demand, via the
    /// compiler's own naming, when a state machine attribute names one) type index every cross-assembly/
    /// interface resolution looks up by full name.</summary>
    sealed class AssemblyUniverse : IDisposable
    {
        const string AsyncStateMachineAttributeName = "AsyncStateMachineAttribute";
        const string IteratorStateMachineAttributeName = "IteratorStateMachineAttribute";
        const string CompilerServicesNamespace = "System.Runtime.CompilerServices";

        readonly List<Stream> streams = [];
        readonly List<PEReader> peReaders = [];
        readonly Dictionary<string, (int AssemblyIndex, TypeDefinitionHandle Handle)> typesByFullName = new(StringComparer.Ordinal);

        public IReadOnlyList<MetadataReader> Readers { get; }
        public IReadOnlyList<PEReader> PeReaders => peReaders;

        public AssemblyUniverse(IReadOnlyList<Assembly> assemblies)
        {
            var readers = new List<MetadataReader>(assemblies.Count);

            for (var assemblyIndex = 0; assemblyIndex < assemblies.Count; assemblyIndex++)
            {
                var stream = File.OpenRead(assemblies[assemblyIndex].Location);
                streams.Add(stream);

                var peReader = new PEReader(stream);
                peReaders.Add(peReader);

                var reader = peReader.GetMetadataReader();
                readers.Add(reader);

                foreach (var typeHandle in reader.TypeDefinitions)
                {
                    var typeDef = reader.GetTypeDefinition(typeHandle);
                    if (!typeDef.GetDeclaringType().IsNil)
                        continue; // Nested (incl. every compiler-generated state machine) — never a lookup target.

                    // TryAdd: a duplicate full name across production assemblies never occurs in
                    // practice (interfaces/classes are each defined exactly once), and silently
                    // keeping the FIRST hit is safer than an exception mid-BFS-setup over a shape
                    // this scan does not need to be exhaustive about.
                    typesByFullName.TryAdd(FullName(reader, typeHandle), (assemblyIndex, typeHandle));
                }
            }

            Readers = readers;
        }

        public (int AssemblyIndex, MethodDefinitionHandle Handle) FindDeclaredMethod(string typeFullName, string methodName)
        {
            if (!typesByFullName.TryGetValue(typeFullName, out var type))
                throw new InvalidOperationException($"GardenerThumbDisjointnessScan root type not found: {typeFullName}");

            var reader = Readers[type.AssemblyIndex];
            var typeDef = reader.GetTypeDefinition(type.Handle);
            foreach (var methodHandle in typeDef.GetMethods())
            {
                if (reader.GetString(reader.GetMethodDefinition(methodHandle).Name) == methodName)
                    return (type.AssemblyIndex, methodHandle);
            }

            throw new InvalidOperationException(
                $"GardenerThumbDisjointnessScan root method not found: {typeFullName}.{methodName}");
        }

        /// <summary>Redirects <paramref name="handle"/> to its own compiler-generated state machine's
        /// <c>MoveNext</c> when one exists — an <c>async</c> or iterator method's REAL body (class
        /// remarks) — otherwise returns <paramref name="handle"/> unchanged (an ordinary synchronous
        /// method, walked directly). Resolved via <c>[AsyncStateMachine]</c>/<c>[IteratorStateMachine]</c>
        /// read directly off THIS specific <paramref name="handle"/> (T367 review HIGH-1/MED-1) — never
        /// a name-prefix guess over sibling nested types, which is blind to lambda/local-function state
        /// machines and ambiguous across overloads.</summary>
        public MethodDefinitionHandle ResolveEffectiveBody(int assemblyIndex, MethodDefinitionHandle handle)
        {
            var reader = Readers[assemblyIndex];
            var method = reader.GetMethodDefinition(handle);

            var stateMachineTypeName = TryGetStateMachineTypeName(reader, method);
            if (stateMachineTypeName is null)
                return handle; // Not state-machine-backed — an ordinary method, walked as-is.

            var stateMachineHandle = ResolveTypeByCompilerName(assemblyIndex, stateMachineTypeName);
            if (stateMachineHandle is null)
            {
                throw new InvalidOperationException(
                    $"[AsyncStateMachine]/[IteratorStateMachine] on {FullName(reader, method.GetDeclaringType())}." +
                    $"{reader.GetString(method.Name)} names '{stateMachineTypeName}', which does not resolve to a " +
                    "type in the same assembly — the scan cannot trust its own redirect here.");
            }

            var stateMachineTypeDef = reader.GetTypeDefinition(stateMachineHandle.Value);
            foreach (var stateMachineMethodHandle in stateMachineTypeDef.GetMethods())
            {
                if (reader.GetString(reader.GetMethodDefinition(stateMachineMethodHandle).Name) == "MoveNext")
                    return stateMachineMethodHandle;
            }

            throw new InvalidOperationException($"State machine type '{stateMachineTypeName}' has no MoveNext method.");
        }

        /// <summary>Reads <paramref name="method"/>'s own <c>AsyncStateMachineAttribute</c> or
        /// <c>IteratorStateMachineAttribute</c> (whichever is present — a method is never both), each a
        /// single-constructor-argument attribute whose one <see cref="Type"/> argument is serialized as
        /// a plain <c>SerString</c> (ECMA-335 II.23.3) naming the state machine type by its own
        /// compiler-emitted name (<c>Outer+Nested</c> for a nested type — <see cref="ResolveTypeByCompilerName"/>
        /// parses that shape). <see langword="null"/> when neither attribute is present (an ordinary,
        /// non-state-machine method).</summary>
        static string? TryGetStateMachineTypeName(MetadataReader reader, MethodDefinition method)
        {
            foreach (var attributeHandle in method.GetCustomAttributes())
            {
                var attribute = reader.GetCustomAttribute(attributeHandle);
                if (attribute.Constructor.Kind != HandleKind.MemberReference)
                    continue; // Every state-machine attribute is a BCL type, always referenced via MemberReference here.

                var constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if (constructor.Parent.Kind != HandleKind.TypeReference)
                    continue;

                var declaringType = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
                if (reader.GetString(declaringType.Namespace) != CompilerServicesNamespace)
                    continue;

                var attributeName = reader.GetString(declaringType.Name);
                if (attributeName != AsyncStateMachineAttributeName && attributeName != IteratorStateMachineAttributeName)
                    continue;

                var blob = reader.GetBlobReader(attribute.Value);
                blob.ReadUInt16(); // Custom attribute prolog (0x0001), ECMA-335 II.23.3.
                return blob.ReadSerializedString();
            }

            return null;
        }

        /// <summary>Resolves a compiler-emitted type name (<c>Namespace.Outer+Nested+...</c> — the exact
        /// shape <see cref="Type.FullName"/>/a custom attribute's own serialized <see cref="Type"/>
        /// argument uses for nested types, including EVERY compiler-generated closure/state-machine
        /// layer: <c>&lt;&gt;c__DisplayClassN_M</c>, <c>&lt;&gt;c</c>, <c>&lt;Method&gt;d__N</c>,
        /// <c>&lt;&lt;Method&gt;b__0_0&gt;d</c>, ...) to a <see cref="TypeDefinitionHandle"/> within
        /// <paramref name="assemblyIndex"/> — state machine types are always emitted in the SAME module
        /// as the method that generates them, so no cross-assembly case exists to handle. Descends one
        /// <c>+</c>-separated segment at a time via <see cref="TypeDefinition.GetNestedTypes"/>, so this
        /// reaches ANY nesting depth the compiler chose (an async lambda's state machine nested inside
        /// its own closure class nested inside the declaring type, for instance) without this scan ever
        /// having to guess or special-case that shape itself.</summary>

        TypeDefinitionHandle? ResolveTypeByCompilerName(int assemblyIndex, string compilerTypeName)
        {
            var commaIndex = compilerTypeName.IndexOf(',', StringComparison.Ordinal);
            if (commaIndex >= 0)
                compilerTypeName = compilerTypeName[..commaIndex]; // Strip a defensive assembly-qualification suffix, if present.

            var segments = compilerTypeName.Split('+');
            if (!typesByFullName.TryGetValue(segments[0], out var outer) || outer.AssemblyIndex != assemblyIndex)
                return null;

            var reader = Readers[assemblyIndex];
            var currentHandle = outer.Handle;

            for (var i = 1; i < segments.Length; i++)
            {
                var currentDef = reader.GetTypeDefinition(currentHandle);
                TypeDefinitionHandle? next = null;
                foreach (var nestedHandle in currentDef.GetNestedTypes())
                {
                    if (reader.GetString(reader.GetTypeDefinition(nestedHandle).Name) == segments[i])
                    {
                        next = nestedHandle;
                        break;
                    }
                }

                if (next is null)
                    return null;

                currentHandle = next.Value;
            }

            return currentHandle;
        }

        /// <summary>Resolves one raw IL call-target token (as read by <see cref="ReadMethodCallTargets"/>)
        /// to zero or more further BFS nodes: a same-assembly <see cref="MethodDefinitionHandle"/>
        /// resolves directly; a cross-assembly <see cref="MemberReferenceHandle"/> resolves via the
        /// global type index, crossing into <paramref name="interfaceAdapters"/>'s effective adapter
        /// when the target type is an interface; a <see cref="MethodSpecificationHandle"/> (a generic
        /// method call) is unwrapped to its underlying method first. Anything else (a
        /// <see cref="TypeSpecificationHandle"/>-parented member, an unindexed BCL/third-party type, a
        /// field/type token that slipped through) yields nothing — a dead end (class remarks' own
        /// documented boundary). A GenWave interface with NO resolvable adapter instead appends to
        /// <paramref name="unresolvedPortViolations"/> (T367 review MED-2) rather than dead-ending
        /// silently.</summary>
        public IEnumerable<(int AssemblyIndex, MethodDefinitionHandle Handle)> ResolveCallTargets(
            int callerAssemblyIndex, EntityHandle token, IReadOnlyDictionary<string, string> interfaceAdapters,
            ICollection<LawViolation> unresolvedPortViolations)
        {
            var reader = Readers[callerAssemblyIndex];

            if (token.Kind == HandleKind.MethodSpecification)
                token = reader.GetMethodSpecification((MethodSpecificationHandle)token).Method;

            if (token.Kind == HandleKind.MethodDefinition)
            {
                yield return (callerAssemblyIndex, (MethodDefinitionHandle)token);
                yield break;
            }

            if (token.Kind != HandleKind.MemberReference)
                yield break;

            var member = reader.GetMemberReference((MemberReferenceHandle)token);
            if (member.Parent.Kind != HandleKind.TypeReference)
                yield break; // TypeSpecification (closed generic) or other — documented boundary.

            var memberName = reader.GetString(member.Name);
            var typeReference = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
            var typeFullName = FullName(reader, typeReference);

            if (!typesByFullName.TryGetValue(typeFullName, out var target))
                yield break; // Not one of OUR production types (BCL/Npgsql/Dapper/...) — dead end.

            var targetReader = Readers[target.AssemblyIndex];
            var targetTypeDef = targetReader.GetTypeDefinition(target.Handle);

            if ((targetTypeDef.Attributes & TypeAttributes.Interface) != 0)
            {
                if (!interfaceAdapters.TryGetValue(typeFullName, out var adapterFullName)
                    || !typesByFullName.TryGetValue(adapterFullName, out var adapterTarget))
                {
                    // MED-2: a GenWave interface this scan cannot resolve to a live adapter is an
                    // opaque seam, not proof of safety — red the law naming the port, never truncate.
                    unresolvedPortViolations.Add(new LawViolation(
                        DisjointnessLawId,
                        typeFullName,
                        $"reachable via {memberName}() with no resolvable DI adapter in this scan's " +
                        "interfaceAdapters map (SPEC F155.3) — an opaque or unregistered seam cannot be " +
                        "proven safe, so this scan reds here rather than silently stopping"));
                    yield break;
                }

                foreach (var candidate in FindMethodsByName(adapterTarget.AssemblyIndex, adapterTarget.Handle, memberName))
                    yield return (adapterTarget.AssemblyIndex, candidate);

                yield break;
            }

            foreach (var candidate in FindMethodsByName(target.AssemblyIndex, target.Handle, memberName))
                yield return (target.AssemblyIndex, candidate);
        }

        IEnumerable<MethodDefinitionHandle> FindMethodsByName(int assemblyIndex, TypeDefinitionHandle typeHandle, string name)
        {
            var reader = Readers[assemblyIndex];
            var typeDef = reader.GetTypeDefinition(typeHandle);
            foreach (var methodHandle in typeDef.GetMethods())
            {
                if (reader.GetString(reader.GetMethodDefinition(methodHandle).Name) == name)
                    yield return methodHandle;
            }
        }

        public static string FullName(MetadataReader reader, TypeDefinitionHandle handle)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            var ns = reader.GetString(typeDef.Namespace);
            var name = reader.GetString(typeDef.Name);
            return ns.Length == 0 ? name : $"{ns}.{name}";
        }

        static string FullName(MetadataReader reader, TypeReference typeReference)
        {
            var ns = reader.GetString(typeReference.Namespace);
            var name = reader.GetString(typeReference.Name);
            return ns.Length == 0 ? name : $"{ns}.{name}";
        }

        public void Dispose()
        {
            foreach (var peReader in peReaders) peReader.Dispose();
            foreach (var stream in streams) stream.Dispose();
        }
    }
}
