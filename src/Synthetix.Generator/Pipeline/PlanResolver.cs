namespace Synthetix.Pipeline;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Synthetix.Diagnostics;
using Synthetix.Models;

/// <summary>
/// Stage 3 of the pipeline: it turns a <see cref="MapperModel"/> into a finished
/// <see cref="MapperPlan"/>.
/// </summary>
/// <remarks>
/// This is where all the real thinking happens. For every mapping method it
/// works out, member by member, where each value comes from - same-name match,
/// flatten path, a custom attribute, or a constructor argument - and records the
/// result as a ready-to-print expression. It never writes any C# itself; that is
/// Stage 4's job. Keeping the two apart makes both easy to test.
/// </remarks>
internal static class PlanResolver
{
    /// <summary>Builds the plan for one whole mapper class.</summary>
    public static MapperPlan Resolve(MapperModel model, CancellationToken ct)
    {
        // If the mapper class is not partial we cannot add any code to it, so we
        // only carry the diagnostics forward and emit nothing.
        if (!model.IsPartial)
        {
            return new MapperPlan(
                model.Namespace, model.Name, model.TypeKeyword, model.Accessibility,
                model.IsStatic, model.ContainingTypes, model.HintName,
                CanEmit: false,
                Methods: EquatableArray<MethodPlan>.Empty,
                MapperDiagnostics: model.StructuralDiagnostics,
                Location: model.Location);
        }

        // One registry per mapper - collection helpers are shared and
        // de-duplicated across all of the mapper's methods.
        var collections = new CollectionHelperRegistry();

        var methodPlans = new List<MethodPlan>();
        foreach (MappingMethodModel method in model.Methods)
        {
            ct.ThrowIfCancellationRequested();
            methodPlans.Add(ResolveMethod(method, model, collections));
        }

        return new MapperPlan(
            model.Namespace, model.Name, model.TypeKeyword, model.Accessibility,
            model.IsStatic, model.ContainingTypes, model.HintName,
            CanEmit: true,
            Methods: methodPlans.ToEquatableArray(),
            MapperDiagnostics: model.StructuralDiagnostics,
            Location: model.Location,
            CollectionHelpers: collections.Plans.ToEquatableArray());
    }

    /// <summary>Builds the plan for one mapping method.</summary>
    private static MethodPlan ResolveMethod(
        MappingMethodModel method, MapperModel model, CollectionHelperRegistry registry)
    {
        var diagnostics = new List<DiagnosticInfo>();
        TypeModel source = method.SourceType;
        TypeModel target = method.TargetType;

        // An IQueryable projection property is built as one inline lambda - a
        // separate path that produces an expression, not statements.
        if (method.IsProjection)
        {
            return ResolveProjectionMethod(method, model, diagnostics);
        }

        // A method carrying [MapDerivedType] entries is a polymorphic dispatcher
        // and is built completely differently - see ResolvePolymorphicMethod.
        if (method.IsPolymorphic)
        {
            return ResolvePolymorphicMethod(method, model, diagnostics);
        }

        // An update method fills an existing instance - also a separate path.
        if (method.IsUpdate)
        {
            return ResolveUpdateMethod(method, model, registry, diagnostics);
        }

        // Both sides have to be classes/structs/records we can look inside.
        if (target.Category != TypeCategory.Complex || source.Category != TypeCategory.Complex)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.NoMappingPossible, method.Location,
                source.DisplayName, target.DisplayName));
            return FailedPlan(method, diagnostics);
        }

        // ----- Read and check the member-level configuration -----
        var configs = new MethodConfiguration(method, target, source, diagnostics);

        // ----- Pick a constructor for the target type -----
        ConstructorModel? constructor = ChooseConstructor(target, source, configs, model);
        if (constructor is null)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.NoUsableConstructor, method.Location, target.DisplayName));
            return FailedPlan(method, diagnostics);
        }

        // The names of the members the constructor already fills (case-insensitive).
        var constructorFilled = new HashSet<string>(
            constructor.Parameters.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

        // Tracks which top-level source members ended up being used.
        var usedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ----- Resolve the constructor arguments -----
        var constructorArguments = new List<Assignment>();
        foreach (ParameterModel parameter in constructor.Parameters)
        {
            Assignment argument = ResolveConstructorArgument(
                parameter, method, model, configs, usedSources, diagnostics, registry);
            constructorArguments.Add(argument);
        }

        // ----- Resolve every settable target member the constructor did not fill -----
        var initAssignments = new List<Assignment>();
        var postAssignments = new List<Assignment>();
        var ignoredTargets = new List<IgnoredMember>();
        int mapped = 0;
        int ignored = 0;
        int unmapped = 0;

        foreach (MemberModel targetMember in target.Members)
        {
            if (!targetMember.CanWrite)
            {
                continue;
            }

            if (constructorFilled.Contains(targetMember.Name))
            {
                // Already handled as a constructor argument.
                mapped++;
                continue;
            }

            MemberOutcome outcome = ResolveTargetMember(
                targetMember, method, model, configs, usedSources, diagnostics, registry);

            if (outcome.Ignored is not null)
            {
                ignoredTargets.Add(outcome.Ignored);
                ignored++;
            }
            else if (outcome.Assignment is not null)
            {
                if (targetMember.MustAssignInInitializer)
                {
                    initAssignments.Add(outcome.Assignment);
                }
                else
                {
                    postAssignments.Add(outcome.Assignment);
                }

                mapped++;
            }
            else
            {
                unmapped++;
            }
        }

        // ----- Diagnostics for source members that nothing used (SYNTX005) -----
        ReportUnusedSources(source, configs, usedSources, model.Options, diagnostics);

        // Does any value need to be awaited? A conversion through an async
        // mapping leaves an "await ..." in the expression (design doc 7.11).
        bool usesAwait =
            AnyAwait(constructorArguments) ||
            AnyAwait(initAssignments) ||
            AnyAwait(postAssignments);

        // SYNTX024: a synchronous method cannot await anything. If one of its
        // values needs an async conversion, report a clear error and emit a
        // throwing stub instead of code that would not compile.
        if (usesAwait && !method.IsAsyncMethod)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.AsyncMappingInSyncMethod, method.Location, method.MethodName));
            return FailedPlan(method, diagnostics);
        }

        return new MethodPlan(
            MethodName: method.MethodName,
            Accessibility: method.Accessibility,
            IsStatic: method.IsStatic,
            ReturnTypeDisplay: method.ReturnTypeDisplay,
            SourceTypeDisplay: method.SourceParameterTypeDisplay,
            SourceParameterName: method.SourceParameterName,
            TargetTypeDisplay: target.DisplayName,
            HasValidBody: true,
            ConstructorArguments: constructorArguments.ToEquatableArray(),
            InitializerAssignments: initAssignments.ToEquatableArray(),
            PostAssignments: postAssignments.ToEquatableArray(),
            IgnoredTargets: ignoredTargets.ToEquatableArray(),
            IgnoredSources: configs.IgnoredSourceMembers.ToEquatableArray(),
            CoverageMapped: mapped,
            CoverageIgnored: ignored,
            CoverageUnmapped: unmapped,
            Diagnostics: diagnostics.ToEquatableArray(),
            IsAsyncMethod: method.IsAsyncMethod,
            ReturnsValueTask: method.ReturnsValueTask,
            UsesAwait: usesAwait,
            CancellationTokenName: method.CancellationTokenName);
    }

    // ===================== existing-instance update =====================

    /// <summary>
    /// Builds the plan for a `void Update(source, target)` method, which fills an
    /// object that already exists rather than creating one (design doc 7.8).
    /// </summary>
    private static MethodPlan ResolveUpdateMethod(
        MappingMethodModel method,
        MapperModel model,
        CollectionHelperRegistry registry,
        List<DiagnosticInfo> diagnostics)
    {
        TypeModel source = method.SourceType;
        TypeModel target = method.TargetType;

        if (target.Category != TypeCategory.Complex || source.Category != TypeCategory.Complex)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.NoMappingPossible, method.Location,
                source.DisplayName, target.DisplayName));
            return FailedPlan(method, diagnostics);
        }

        var configs = new MethodConfiguration(method, target, source, diagnostics);
        var usedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assignments = new List<Assignment>();
        var ignoredTargets = new List<IgnoredMember>();
        int mapped = 0;
        int ignored = 0;
        int unmapped = 0;

        foreach (MemberModel targetMember in target.Members)
        {
            if (!targetMember.CanWrite)
            {
                continue;
            }

            // An init-only member can only be set while an object is created.
            // On an existing instance it cannot be touched - SYNTX022, and skip.
            if (targetMember.IsInitOnly)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.MemberNotUpdatable, method.Location, targetMember.Name));
                continue;
            }

            MemberOutcome outcome = ResolveTargetMember(
                targetMember, method, model, configs, usedSources, diagnostics, registry);

            if (outcome.Ignored is not null)
            {
                ignoredTargets.Add(outcome.Ignored);
                ignored++;
            }
            else if (outcome.Assignment is not null)
            {
                assignments.Add(outcome.Assignment);
                mapped++;
            }
            else
            {
                unmapped++;
            }
        }

        ReportUnusedSources(source, configs, usedSources, model.Options, diagnostics);

        // SYNTX024: a void update method is never async, so it cannot await an
        // async conversion. Report the error and emit a throwing stub.
        if (AnyAwait(assignments))
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.AsyncMappingInSyncMethod, method.Location, method.MethodName));
            return FailedPlan(method, diagnostics);
        }

        return new MethodPlan(
            MethodName: method.MethodName,
            Accessibility: method.Accessibility,
            IsStatic: method.IsStatic,
            ReturnTypeDisplay: "void",
            SourceTypeDisplay: method.SourceParameterTypeDisplay,
            SourceParameterName: method.SourceParameterName,
            TargetTypeDisplay: target.DisplayName,
            HasValidBody: true,
            ConstructorArguments: EquatableArray<Assignment>.Empty,
            InitializerAssignments: EquatableArray<Assignment>.Empty,
            PostAssignments: assignments.ToEquatableArray(),
            IgnoredTargets: ignoredTargets.ToEquatableArray(),
            IgnoredSources: configs.IgnoredSourceMembers.ToEquatableArray(),
            CoverageMapped: mapped,
            CoverageIgnored: ignored,
            CoverageUnmapped: unmapped,
            Diagnostics: diagnostics.ToEquatableArray(),
            IsUpdate: true,
            TargetParameterName: method.TargetParameterName);
    }

    // ===================== IQueryable projection =====================

    /// <summary>
    /// Builds the plan for an IQueryable projection property (design doc 7.10).
    /// The whole body is one object-initializer lambda; if the mapping cannot be
    /// expressed as an expression tree, SYNTX023 is raised and a stub is emitted.
    /// </summary>
    private static MethodPlan ResolveProjectionMethod(
        MappingMethodModel method, MapperModel model, List<DiagnosticInfo> diagnostics)
    {
        string? body = ProjectionResolver.Resolve(
            method.SourceType,
            method.TargetType,
            method.SourceParameterName,
            model,
            method.Configurations,
            diagnostics,
            method.Location,
            method.MethodName);

        return new MethodPlan(
            MethodName: method.MethodName,
            Accessibility: method.Accessibility,
            IsStatic: method.IsStatic,
            ReturnTypeDisplay: method.ReturnTypeDisplay,
            SourceTypeDisplay: method.SourceParameterTypeDisplay,
            SourceParameterName: method.SourceParameterName,
            TargetTypeDisplay: method.TargetType.DisplayName,
            // A null body means SYNTX023 was raised - the emitter writes a stub.
            HasValidBody: body is not null,
            ConstructorArguments: EquatableArray<Assignment>.Empty,
            InitializerAssignments: EquatableArray<Assignment>.Empty,
            PostAssignments: EquatableArray<Assignment>.Empty,
            IgnoredTargets: EquatableArray<IgnoredMember>.Empty,
            IgnoredSources: EquatableArray<IgnoredMember>.Empty,
            CoverageMapped: 0,
            CoverageIgnored: 0,
            CoverageUnmapped: 0,
            Diagnostics: diagnostics.ToEquatableArray(),
            IsProjection: true,
            ProjectionBody: body ?? string.Empty);
    }

    // ===================== polymorphic dispatch =====================

    /// <summary>
    /// Builds the plan for a polymorphic mapping method - one that carries
    /// [MapDerivedType] entries (design doc 7.7). Its body becomes a check of the
    /// runtime type that hands off to the matching derived mapping.
    /// </summary>
    private static MethodPlan ResolvePolymorphicMethod(
        MappingMethodModel method, MapperModel model, List<DiagnosticInfo> diagnostics)
    {
        var dispatches = new List<DerivedDispatch>();

        // One dispatch arm per [MapDerivedType] entry.
        foreach (DerivedTypeMapping derived in method.DerivedTypes)
        {
            string? mappingMethod = FindDerivedMappingMethod(derived, model);
            if (mappingMethod is null)
            {
                // SYNTX021: the derived pair has no mapping the generator can call.
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.InvalidDerivedTypeMapping,
                    derived.Location ?? method.Location,
                    method.MethodName,
                    "no mapping was found from '" + derived.SourceTypeDisplay +
                    "' to '" + derived.TargetTypeDisplay + "'"));
                continue;
            }

            dispatches.Add(new DerivedDispatch(derived.SourceTypeDisplay, mappingMethod));
        }

        // SYNTX020: every concrete subtype found in the compilation must have an
        // entry, so no subtype can be mapped as its base by accident.
        var coveredKeys = new HashSet<string>(
            method.DerivedTypes.Select(d => d.SourceTypeKey), StringComparer.Ordinal);
        foreach (DerivedTypeRef discovered in method.DiscoveredDerivedTypes)
        {
            if (!coveredKeys.Contains(discovered.TypeKey))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.NonExhaustivePolymorphicMapping,
                    method.Location, method.MethodName, discovered.DisplayName));
            }
        }

        return new MethodPlan(
            MethodName: method.MethodName,
            Accessibility: method.Accessibility,
            IsStatic: method.IsStatic,
            ReturnTypeDisplay: method.ReturnTypeDisplay,
            SourceTypeDisplay: method.SourceParameterTypeDisplay,
            SourceParameterName: method.SourceParameterName,
            TargetTypeDisplay: method.TargetType.DisplayName,
            HasValidBody: true,
            ConstructorArguments: EquatableArray<Assignment>.Empty,
            InitializerAssignments: EquatableArray<Assignment>.Empty,
            PostAssignments: EquatableArray<Assignment>.Empty,
            IgnoredTargets: EquatableArray<IgnoredMember>.Empty,
            IgnoredSources: EquatableArray<IgnoredMember>.Empty,
            CoverageMapped: dispatches.Count,
            CoverageIgnored: 0,
            CoverageUnmapped: 0,
            Diagnostics: diagnostics.ToEquatableArray(),
            IsPolymorphic: true,
            DerivedDispatches: dispatches.ToEquatableArray());
    }

    /// <summary>Finds the mapping method that converts one derived-type pair.</summary>
    private static string? FindDerivedMappingMethod(DerivedTypeMapping derived, MapperModel model)
    {
        // First preference: a sibling partial mapping method on the same mapper.
        foreach (MappingMethodModel sibling in model.Methods)
        {
            if (sibling.SourceType.IdentityKey == derived.SourceTypeKey &&
                sibling.TargetType.IdentityKey == derived.TargetTypeKey)
            {
                return sibling.MethodName;
            }
        }

        // Otherwise: a user-written mapping method.
        foreach (UserMappingModel user in model.UserMappings)
        {
            if (!user.IsIgnored &&
                user.ParameterTypeKey == derived.SourceTypeKey &&
                user.ReturnTypeKey == derived.TargetTypeKey)
            {
                return user.MethodName;
            }
        }

        return null;
    }

    // ===================== constructor handling =====================

    /// <summary>Picks the constructor Synthetix will use to build the target object.</summary>
    private static ConstructorModel? ChooseConstructor(
        TypeModel target, TypeModel source, MethodConfiguration configs, MapperModel model)
    {
        var accessible = target.Constructors.Where(c => c.IsAccessible).ToList();

        // A parameterless constructor is always the simplest choice.
        ConstructorModel? parameterless = accessible.FirstOrDefault(c => c.Parameters.Count == 0);
        if (parameterless is not null)
        {
            return parameterless;
        }

        // Otherwise pick the smallest constructor whose every parameter we can fill.
        foreach (ConstructorModel candidate in accessible.OrderBy(c => c.Parameters.Count))
        {
            if (candidate.Parameters.All(p => CanFillParameter(p, source, configs, model)))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>True when a constructor parameter has some value we can give it.</summary>
    private static bool CanFillParameter(
        ParameterModel parameter, TypeModel source, MethodConfiguration configs, MapperModel model)
    {
        // A [MapValue] or [MapProperty] aimed at the parameter name.
        if (configs.MapValues.ContainsKey(parameter.Name) ||
            configs.MapProperties.ContainsKey(parameter.Name))
        {
            return true;
        }

        // A source member with the same name (constructor matching is case-insensitive).
        return source.FindMember(parameter.Name, ignoreCase: true) is not null;
    }

    /// <summary>Works out the value for one constructor parameter.</summary>
    private static Assignment ResolveConstructorArgument(
        ParameterModel parameter,
        MappingMethodModel method,
        MapperModel model,
        MethodConfiguration configs,
        HashSet<string> usedSources,
        List<DiagnosticInfo> diagnostics,
        CollectionHelperRegistry registry)
    {
        string sourceParam = method.SourceParameterName;

        // A [MapValue] wins first.
        if (configs.MapValues.TryGetValue(parameter.Name, out MemberConfig? valueConfig))
        {
            return BuildMapValueAssignment(parameter.Name, valueConfig, model, diagnostics);
        }

        // Then a [MapProperty].
        if (configs.MapProperties.TryGetValue(parameter.Name, out MemberConfig? propertyConfig))
        {
            Assignment? fromProperty = BuildMapPropertyAssignment(
                parameter.Name, parameter.Type, propertyConfig, method, model, usedSources, diagnostics,
                MappingRuleKind.ConstructorParameter, registry);
            if (fromProperty is not null)
            {
                return fromProperty;
            }
        }

        // Then a source member with the same name.
        MemberModel? sameNameSource = method.SourceType.FindMember(parameter.Name, ignoreCase: true);
        if (sameNameSource is not null && sameNameSource.CanRead)
        {
            usedSources.Add(sameNameSource.Name);

            // A collection parameter is deep-mapped element by element.
            if (sameNameSource.Type.IsCollection && parameter.Type.IsCollection)
            {
                string? collectionValue = CollectionResolver.Resolve(
                    sameNameSource.Type, parameter.Type, sourceParam + "." + sameNameSource.Name,
                    model, registry, diagnostics, method.Location, parameter.Name);
                if (collectionValue is not null)
                {
                    return new Assignment(
                        parameter.Name, collectionValue, MappingRuleKind.CollectionMapping,
                        sourceParam + "." + sameNameSource.Name);
                }
            }

            ConversionResult conversion = ConversionResolver.Convert(
                sourceParam + "." + sameNameSource.Name,
                sameNameSource.Type,
                sameNameSource.IsNullableReference || sameNameSource.Type.IsNullableValueType,
                parameter.Type,
                parameter.IsNullableReference,
                model,
                method.Location,
                sameNameSource.Name,
                parameter.Name);

            if (conversion.Diagnostic is not null)
            {
                diagnostics.Add(conversion.Diagnostic);
            }

            if (conversion.Succeeded)
            {
                return new Assignment(
                    parameter.Name, conversion.Expression!, MappingRuleKind.ConstructorParameter,
                    sourceParam + "." + sameNameSource.Name);
            }
        }

        // Nothing fit. ChooseConstructor should have prevented this, but stay safe.
        return new Assignment(
            parameter.Name, "default!", MappingRuleKind.ConstructorParameter, "(unresolved)");
    }

    // ===================== target member handling =====================

    /// <summary>The result of resolving one target member.</summary>
    private readonly struct MemberOutcome
    {
        private MemberOutcome(Assignment? assignment, IgnoredMember? ignored)
        {
            Assignment = assignment;
            Ignored = ignored;
        }

        public Assignment? Assignment { get; }

        public IgnoredMember? Ignored { get; }

        public static MemberOutcome Mapped(Assignment assignment) => new(assignment, null);

        public static MemberOutcome WasIgnored(IgnoredMember ignored) => new(null, ignored);

        public static MemberOutcome Unmapped() => new(null, null);
    }

    /// <summary>
    /// Resolves one settable target member, following the precedence order from
    /// design doc 7.4: MapValue, then MapProperty, then ignore, then same-name,
    /// then the flatten / unflatten convention.
    /// </summary>
    private static MemberOutcome ResolveTargetMember(
        MemberModel targetMember,
        MappingMethodModel method,
        MapperModel model,
        MethodConfiguration configs,
        HashSet<string> usedSources,
        List<DiagnosticInfo> diagnostics,
        CollectionHelperRegistry registry)
    {
        string name = targetMember.Name;
        string sourceParam = method.SourceParameterName;
        TypeModel source = method.SourceType;

        // 1. [MapValue] - highest precedence.
        if (configs.MapValues.TryGetValue(name, out MemberConfig? valueConfig))
        {
            return MemberOutcome.Mapped(BuildMapValueAssignment(name, valueConfig, model, diagnostics));
        }

        // 2. [MapProperty].
        if (configs.MapProperties.TryGetValue(name, out MemberConfig? propertyConfig))
        {
            Assignment? assignment = BuildMapPropertyAssignment(
                name, targetMember.Type, propertyConfig, method, model, usedSources, diagnostics,
                MappingRuleKind.MapProperty, registry);
            return assignment is not null ? MemberOutcome.Mapped(assignment) : MemberOutcome.Unmapped();
        }

        // 3. [MapperIgnoreTarget].
        if (configs.IgnoredTargetNames.Contains(name))
        {
            return MemberOutcome.WasIgnored(new IgnoredMember(name, "[MapperIgnoreTarget]"));
        }

        // 4. Same-name match.
        MemberModel? sameNameSource = source.FindMember(
            name, model.Options.NameMatching == PropertyNameMatching.IgnoreCase);
        if (sameNameSource is not null && sameNameSource.CanRead)
        {
            usedSources.Add(sameNameSource.Name);

            // A collection member is deep-mapped element by element.
            if (sameNameSource.Type.IsCollection && targetMember.Type.IsCollection)
            {
                string? collectionValue = CollectionResolver.Resolve(
                    sameNameSource.Type, targetMember.Type, sourceParam + "." + sameNameSource.Name,
                    model, registry, diagnostics, method.Location, name);
                return collectionValue is not null
                    ? MemberOutcome.Mapped(new Assignment(
                        name, collectionValue, MappingRuleKind.CollectionMapping,
                        sourceParam + "." + sameNameSource.Name))
                    : MemberOutcome.Unmapped();
            }

            ConversionResult conversion = ConversionResolver.Convert(
                sourceParam + "." + sameNameSource.Name,
                sameNameSource.Type,
                sameNameSource.IsNullableReference || sameNameSource.Type.IsNullableValueType,
                targetMember.Type,
                targetMember.IsNullableReference,
                model,
                method.Location,
                sameNameSource.Name,
                name);

            if (conversion.Diagnostic is not null)
            {
                diagnostics.Add(conversion.Diagnostic);
            }

            if (conversion.Succeeded)
            {
                MappingRuleKind rule = conversion.Kind switch
                {
                    ConversionKind.UserMapping => MappingRuleKind.UserMapping,
                    ConversionKind.NestedMapping => MappingRuleKind.NestedMapping,
                    _ => MappingRuleKind.SameName,
                };
                return MemberOutcome.Mapped(new Assignment(
                    name, conversion.Expression!, rule, sourceParam + "." + sameNameSource.Name));
            }

            // A same-name member exists but the types do not fit. The error was
            // recorded above; treat the member as unmapped.
            return MemberOutcome.Unmapped();
        }

        // 5. Flatten: collapse a nested source path into this flat target name.
        if (model.Options.EnableFlattening)
        {
            MemberOutcome flattened = TryFlatten(
                targetMember, method, model, usedSources, diagnostics);
            if (flattened.Assignment is not null)
            {
                return flattened;
            }

            // 6. Unflatten: build this complex member from flat source members.
            if (targetMember.Type.IsComplex)
            {
                UnflattenResult? unflatten = FlatteningResolver.TryUnflatten(
                    targetMember.Type, name, source, sourceParam, model.Options.NameMatching);
                if (unflatten is not null)
                {
                    foreach (string consumed in unflatten.ConsumedSourceNames)
                    {
                        usedSources.Add(consumed);
                    }

                    return MemberOutcome.Mapped(new Assignment(
                        name, unflatten.Expression, MappingRuleKind.UnflattenPath, "(unflattened)"));
                }
            }
        }

        // 7. Nothing produced a value for this member.
        ReportUnmappedTarget(targetMember, method, model.Options, diagnostics);
        return MemberOutcome.Unmapped();
    }

    /// <summary>Tries the flattening convention for one target member.</summary>
    private static MemberOutcome TryFlatten(
        MemberModel targetMember,
        MappingMethodModel method,
        MapperModel model,
        HashSet<string> usedSources,
        List<DiagnosticInfo> diagnostics)
    {
        List<List<MemberModel>> paths = FlatteningResolver.FindFlattenPaths(
            method.SourceType, targetMember.Name, model.Options.NameMatching);

        if (paths.Count == 0)
        {
            return MemberOutcome.Unmapped();
        }

        // More than one path means we cannot tell which one the developer wants.
        if (paths.Count > 1)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.AmbiguousFlattening, method.Location, targetMember.Name));
            return MemberOutcome.Unmapped();
        }

        List<MemberModel> path = paths[0];
        usedSources.Add(path[0].Name);

        string dottedPath = method.SourceParameterName + "." + string.Join(".", path.Select(p => p.Name));
        Assignment? assignment = BuildPathAssignment(
            targetMember, path, method, model, MappingRuleKind.FlattenPath, dottedPath, diagnostics);

        return assignment is not null ? MemberOutcome.Mapped(assignment) : MemberOutcome.Unmapped();
    }

    // ===================== value builders =====================

    /// <summary>Builds the assignment for a [MapValue] - a constant or a method call.</summary>
    private static Assignment BuildMapValueAssignment(
        string targetName, MemberConfig config, MapperModel model, List<DiagnosticInfo> diagnostics)
    {
        if (config.HasConstant && config.ConstantExpression is not null)
        {
            return new Assignment(
                targetName, config.ConstantExpression, MappingRuleKind.ConstantValue,
                "constant " + config.ConstantExpression);
        }

        if (config.Use is not null)
        {
            // The method must exist on the mapper.
            if (!model.DeclaredMethodNames.Contains(config.Use))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.ConverterMethodNotFound,
                    config.Location, config.Use, "(no parameters)", targetName));
            }

            return new Assignment(
                targetName, config.Use + "()", MappingRuleKind.ValueMethod, config.Use + "()");
        }

        // A [MapValue] with neither a constant nor a Use method does nothing useful.
        return new Assignment(targetName, "default!", MappingRuleKind.ConstantValue, "(empty MapValue)");
    }

    /// <summary>Builds the assignment for a [MapProperty] - a rename, a path, or a converter.</summary>
    private static Assignment? BuildMapPropertyAssignment(
        string targetName,
        TypeModel targetType,
        MemberConfig config,
        MappingMethodModel method,
        MapperModel model,
        HashSet<string> usedSources,
        List<DiagnosticInfo> diagnostics,
        MappingRuleKind ruleWhenPlain,
        CollectionHelperRegistry registry)
    {
        if (config.Source is null)
        {
            return null;
        }

        // Resolve the source path (it may be a single name or a dotted path).
        List<MemberModel>? path = ResolveSourcePath(method.SourceType, config.Source, model, out _);
        if (path is null)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.SourcePathNotFound,
                config.Location, config.Source, method.SourceType.DisplayName));
            return null;
        }

        usedSources.Add(path[0].Name);

        // A "Use" converter changes the rule and wraps the value in a method call.
        if (config.Use is not null)
        {
            return BuildConverterAssignment(
                targetName, targetType, path, config, method, model, diagnostics);
        }

        // A plain rename or path: a single member behaves like a same-name match,
        // a longer path behaves like flattening.
        if (path.Count == 1)
        {
            MemberModel only = path[0];
            string sourceExpression = method.SourceParameterName + "." + only.Name;

            // A renamed collection member is deep-mapped element by element.
            if (only.Type.IsCollection && targetType.IsCollection)
            {
                string? collectionValue = CollectionResolver.Resolve(
                    only.Type, targetType, sourceExpression,
                    model, registry, diagnostics, config.Location, targetName);
                return collectionValue is not null
                    ? new Assignment(targetName, collectionValue, MappingRuleKind.CollectionMapping, sourceExpression)
                    : null;
            }

            ConversionResult conversion = ConversionResolver.Convert(
                sourceExpression,
                only.Type,
                only.IsNullableReference || only.Type.IsNullableValueType,
                targetType,
                false,
                model,
                config.Location,
                only.Name,
                targetName);

            if (conversion.Diagnostic is not null)
            {
                diagnostics.Add(conversion.Diagnostic);
            }

            return conversion.Succeeded
                ? new Assignment(targetName, conversion.Expression!, ruleWhenPlain, sourceExpression)
                : null;
        }

        string dotted = method.SourceParameterName + "." + string.Join(".", path.Select(p => p.Name));
        return BuildPathAssignment(
            new MemberModel(targetName, targetType, false, true, true, false, false, false),
            path, method, model, ruleWhenPlain, dotted, diagnostics);
    }

    /// <summary>Builds an assignment that routes a value through a "Use" converter method.</summary>
    private static Assignment? BuildConverterAssignment(
        string targetName,
        TypeModel targetType,
        List<MemberModel> path,
        MemberConfig config,
        MappingMethodModel method,
        MapperModel model,
        List<DiagnosticInfo> diagnostics)
    {
        MemberModel finalMember = path[path.Count - 1];
        string sourceExpression = method.SourceParameterName + "." + string.Join(".", path.Select(p => p.Name));

        // The converter must be a method that accepts the source value and
        // returns the target type.
        bool signatureFits = model.UserMappings.Any(u =>
            u.MethodName == config.Use &&
            !u.IsIgnored &&
            u.ParameterTypeKey == finalMember.Type.IdentityKey &&
            u.ReturnTypeKey == targetType.IdentityKey);

        if (!signatureFits)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.ConverterMethodNotFound,
                config.Location, config.Use ?? "(none)", finalMember.Type.DisplayName, targetType.DisplayName));
            return null;
        }

        string expression = config.Use + "(" + sourceExpression + ")";
        return new Assignment(targetName, expression, MappingRuleKind.Converter, expression);
    }

    /// <summary>
    /// Builds an assignment from a multi-step source path, applying the mapper's
    /// null-path handling and checking the value types line up.
    /// </summary>
    private static Assignment? BuildPathAssignment(
        MemberModel targetMember,
        List<MemberModel> path,
        MappingMethodModel method,
        MapperModel model,
        MappingRuleKind rule,
        string sourceDescription,
        List<DiagnosticInfo> diagnostics)
    {
        FlattenExpression flattened = FlatteningResolver.BuildExpression(
            method.SourceParameterName, path, model.Options.NullPathHandling,
            method.MethodName);

        MemberModel finalMember = flattened.FinalMember;

        // The value at the end of the path must fit the target member's type.
        if (!PathValueFits(finalMember.Type, targetMember.Type))
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.TypeMismatch, method.Location,
                finalMember.Name, finalMember.Type.DisplayName,
                targetMember.Name, targetMember.Type.DisplayName));
            return null;
        }

        string expression = flattened.Expression;

        // Handle a value that might be null along the way.
        if (flattened.MightBeNull)
        {
            bool targetTakesNull = targetMember.Type.IsReferenceType ||
                                   targetMember.Type.IsNullableValueType ||
                                   targetMember.IsNullableReference;

            if (!targetTakesNull)
            {
                // A non-nullable value-type target cannot hold null, so fall back
                // to the default value. With Propagate this is also a warning.
                expression = "(" + expression + ") ?? default(" + targetMember.Type.DisplayName + ")";

                if (model.Options.NullPathHandling == NullHandling.Propagate)
                {
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.NullableToNonNullable, method.Location,
                        finalMember.Name, targetMember.Name));
                }
            }
            else if (!targetMember.IsNullableReference && targetMember.Type.IsReferenceType)
            {
                // A non-nullable reference target: still allowed, but worth a warning.
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.NullableToNonNullable, method.Location,
                    finalMember.Name, targetMember.Name));
            }
        }

        return new Assignment(targetMember.Name, expression, rule, sourceDescription);
    }

    // ===================== diagnostics helpers =====================

    private static void ReportUnmappedTarget(
        MemberModel targetMember,
        MappingMethodModel method,
        MapperOptions options,
        List<DiagnosticInfo> diagnostics)
    {
        // Strict mode: every target member must be accounted for.
        if (options.RequireExplicitMapping)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.ExplicitMappingRequired, method.Location,
                targetMember.Name, method.TargetType.DisplayName));
            return;
        }

        // Otherwise report SYNTX004 at the severity the developer chose.
        if (options.UnmappedTargetMember == MemberMappingSeverity.Ignore)
        {
            return;
        }

        diagnostics.Add(DiagnosticInfo.Create(
            DiagnosticDescriptors.UnmappedTargetMember,
            method.Location,
            SeverityFor(options.UnmappedTargetMember),
            targetMember.Name,
            method.TargetType.DisplayName));
    }

    private static void ReportUnusedSources(
        TypeModel source,
        MethodConfiguration configs,
        HashSet<string> usedSources,
        MapperOptions options,
        List<DiagnosticInfo> diagnostics)
    {
        if (options.UnmappedSourceMember == MemberMappingSeverity.Ignore)
        {
            return;
        }

        foreach (MemberModel member in source.Members)
        {
            if (!member.CanRead ||
                usedSources.Contains(member.Name) ||
                configs.IgnoredSourceNames.Contains(member.Name))
            {
                continue;
            }

            // An unused source member has no single obvious location, so this
            // diagnostic points at the whole file rather than one spot.
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.UnusedSourceMember,
                null,
                SeverityFor(options.UnmappedSourceMember),
                member.Name,
                source.DisplayName));
        }
    }

    private static Microsoft.CodeAnalysis.DiagnosticSeverity SeverityFor(MemberMappingSeverity severity)
        => severity == MemberMappingSeverity.Error
            ? Microsoft.CodeAnalysis.DiagnosticSeverity.Error
            : Microsoft.CodeAnalysis.DiagnosticSeverity.Warning;

    // ===================== small shared helpers =====================

    /// <summary>
    /// Resolves a source path string ("Customer.Name" or just "Total") into the
    /// chain of members it points at. Returns null if any step is missing.
    /// </summary>
    private static List<MemberModel>? ResolveSourcePath(
        TypeModel source, string path, MapperModel model, out string? failedSegment)
    {
        failedSegment = null;
        var members = new List<MemberModel>();
        TypeModel current = source;
        bool ignoreCase = model.Options.NameMatching == PropertyNameMatching.IgnoreCase;

        foreach (string segment in path.Split('.'))
        {
            MemberModel? member = current.FindMember(segment, ignoreCase);
            if (member is null || !member.CanRead)
            {
                failedSegment = segment;
                return null;
            }

            members.Add(member);
            current = member.Type;
        }

        return members.Count > 0 ? members : null;
    }

    /// <summary>
    /// True when any assignment in the list awaits something. A conversion
    /// through an async mapping leaves an "await " in the value expression.
    /// </summary>
    private static bool AnyAwait(IEnumerable<Assignment> assignments)
    {
        foreach (Assignment assignment in assignments)
        {
            if (assignment.ValueExpression.Contains("await "))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A simple "do these path-end types line up" check.</summary>
    private static bool PathValueFits(TypeModel source, TypeModel target)
    {
        TypeModel sourceCore = source.Category == TypeCategory.Nullable ? source.Inner! : source;
        TypeModel targetCore = target.Category == TypeCategory.Nullable ? target.Inner! : target;

        return sourceCore.IdentityKey == targetCore.IdentityKey ||
               target.IdentityKey == "object";
    }

    /// <summary>Builds the throw-stub plan used when a method cannot be mapped at all.</summary>
    /// <remarks>
    /// The stub still has to match the partial method the developer declared, so
    /// every signature-shaping field - the update target parameter, the async
    /// return type, a CancellationToken - is carried straight across. The body
    /// is just a throw, so the method is never marked async (a throw is legal in
    /// a Task-returning method without the async keyword).
    /// </remarks>
    private static MethodPlan FailedPlan(MappingMethodModel method, List<DiagnosticInfo> diagnostics)
        => new(
            MethodName: method.MethodName,
            Accessibility: method.Accessibility,
            IsStatic: method.IsStatic,
            ReturnTypeDisplay: method.ReturnTypeDisplay,
            SourceTypeDisplay: method.SourceParameterTypeDisplay,
            SourceParameterName: method.SourceParameterName,
            TargetTypeDisplay: method.TargetType.DisplayName,
            HasValidBody: false,
            ConstructorArguments: EquatableArray<Assignment>.Empty,
            InitializerAssignments: EquatableArray<Assignment>.Empty,
            PostAssignments: EquatableArray<Assignment>.Empty,
            IgnoredTargets: EquatableArray<IgnoredMember>.Empty,
            IgnoredSources: EquatableArray<IgnoredMember>.Empty,
            CoverageMapped: 0,
            CoverageIgnored: 0,
            CoverageUnmapped: 0,
            Diagnostics: diagnostics.ToEquatableArray(),
            IsUpdate: method.IsUpdate,
            TargetParameterName: method.TargetParameterName,
            CancellationTokenName: method.CancellationTokenName);
}
