namespace Synthetix.Pipeline;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthetix.Diagnostics;
using Synthetix.Models;

/// <summary>
/// Stage 2 of the generator pipeline: it turns a live [Mapper] class into a
/// plain, value-comparable <see cref="MapperModel"/>.
/// </summary>
/// <remarks>
/// This is the only stage that is allowed to touch Roslyn symbols. By the time
/// it returns, every fact the later stages need - names, types, nullability,
/// members, constructors - has been copied into strings, enums, and the model
/// records. No Roslyn symbol is kept, because symbols cannot be compared by
/// value and would break the generator's caching.
/// </remarks>
internal static class ModelBuilder
{
    // How deep into nested types we look. Deep enough for normal object graphs,
    // shallow enough that a strange type can never make us loop forever.
    private const int MaxTypeDepth = 8;

    // The full metadata names of the Synthetix attributes, used to recognise them.
    private const string MapperAttribute = "Synthetix.MapperAttribute";
    private const string MapPropertyAttribute = "Synthetix.MapPropertyAttribute";
    private const string MapperIgnoreTargetAttribute = "Synthetix.MapperIgnoreTargetAttribute";
    private const string MapperIgnoreSourceAttribute = "Synthetix.MapperIgnoreSourceAttribute";
    private const string MapValueAttribute = "Synthetix.MapValueAttribute";
    private const string UserMappingAttribute = "Synthetix.UserMappingAttribute";

    // Names a type using "global::" so it is always safe to drop into generated
    // code. Special types come out as keywords ("int", "string").
    private static readonly SymbolDisplayFormat FullyQualified =
        SymbolDisplayFormat.FullyQualifiedFormat;

    // Like the above, but keeps the nullable "?" so a generated method signature
    // matches the one the developer wrote.
    private static readonly SymbolDisplayFormat FullyQualifiedWithNullable =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    // A plain name with no "global::" prefix, used to recognise well-known types.
    private static readonly SymbolDisplayFormat PlainName =
        SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(
            SymbolDisplayGlobalNamespaceStyle.Omitted);

    /// <summary>
    /// Builds the model for one [Mapper] class. Called once per mapper, per build.
    /// </summary>
    public static MapperModel Build(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        var mapperSymbol = (INamedTypeSymbol)context.TargetSymbol;
        var mapperNode = (TypeDeclarationSyntax)context.TargetNode;

        // ----- Where the mapper lives and how it is declared -----
        string @namespace = mapperSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : mapperSymbol.ContainingNamespace.ToDisplayString();

        string typeKeyword = mapperSymbol.TypeKind == TypeKind.Struct ? "struct" : "class";
        bool isPartial = IsDeclaredPartial(mapperSymbol);
        var mapperLocation = LocationInfo.CreateFrom(mapperNode.Identifier.GetLocation());

        // ----- Read the [Mapper] attribute settings -----
        MapperOptions options = ReadMapperOptions(context.Attributes.FirstOrDefault());

        // ----- Walk every method on the mapper class -----
        var mappingMethods = new List<MappingMethodModel>();
        var userMappings = new List<UserMappingModel>();
        var declaredMethodNames = new List<string>();
        var structuralDiagnostics = new List<DiagnosticInfo>();

        foreach (ISymbol member in mapperSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            // A partial property of type Expression<Func<TSource, TTarget>> is an
            // IQueryable projection the generator must fill in (design doc 7.10).
            if (member is IPropertySymbol property)
            {
                MappingMethodModel? projection = TryBuildProjection(property, ct);
                if (projection is not null)
                {
                    mappingMethods.Add(projection);
                }

                continue;
            }

            if (member is not IMethodSymbol method || method.MethodKind != MethodKind.Ordinary)
            {
                continue;
            }

            // Remember every method name so a "Use = ..." setting can be checked.
            declaredMethodNames.Add(method.Name);

            bool hasConfig = HasAnyConfigAttribute(method);

            // Does the return type look like Task<T> or ValueTask<T>? An async
            // mapping method wraps the real target type in one of these (7.11).
            bool returnIsTaskLike = IsTaskLike(method.ReturnType, out _, out _);

            // A normal mapping method takes one parameter and returns the target
            // directly (not wrapped in a Task).
            bool isNormalShape =
                method.Parameters.Length == 1 && !method.ReturnsVoid && !returnIsTaskLike;
            // An update method takes two parameters and returns void (design doc 7.8).
            bool isUpdateShape = method.Parameters.Length == 2 && method.ReturnsVoid;
            // An async mapping method returns Task<T>/ValueTask<T>. It takes the
            // source, and may take a trailing CancellationToken (design doc 7.11).
            bool isAsyncShape =
                returnIsTaskLike &&
                (method.Parameters.Length == 1 ||
                 (method.Parameters.Length == 2 && IsCancellationToken(method.Parameters[1].Type)));
            bool validShape = isNormalShape || isUpdateShape || isAsyncShape;
            bool isGeneratableMapping =
                method.IsPartialDefinition && method.PartialImplementationPart is null && validShape;

            if (isGeneratableMapping)
            {
                // A partial method with no body and the right shape: the
                // generator will write its body.
                mappingMethods.Add(BuildMappingMethod(method, context.SemanticModel.Compilation, ct));
            }
            else if (hasConfig)
            {
                // The method carries mapping attributes but is not a method we
                // can implement. That is always a mistake - SYNTX002.
                structuralDiagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.MappingMethodMustBePartial,
                    LocationInfo.CreateFrom(method.Locations.FirstOrDefault() ?? Location.None),
                    method.Name));
            }
            else if (!method.IsPartialDefinition && method.PartialDefinitionPart is null &&
                     method.Parameters.Length == 1 && !method.ReturnsVoid)
            {
                // A normal method that converts one type to another: it can be
                // picked up as a user mapping (design doc 4.4). It always takes
                // one parameter and returns a value (possibly a Task<T>, which
                // makes it an async user mapping).
                userMappings.Add(BuildUserMapping(method));
            }
        }

        // ----- Structural diagnostics about the mapper as a whole -----
        if (!isPartial)
        {
            structuralDiagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.MapperMustBePartial, mapperLocation, mapperSymbol.Name));
        }
        else if (mappingMethods.Count == 0)
        {
            // SYNTX016: a [Mapper] with no mapping methods is almost always an oversight.
            structuralDiagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.EmptyMapper, mapperLocation, mapperSymbol.Name));
        }

        return new MapperModel(
            Namespace: @namespace,
            Name: mapperSymbol.Name,
            TypeKeyword: typeKeyword,
            Accessibility: AccessibilityText(mapperSymbol.DeclaredAccessibility),
            IsStatic: mapperSymbol.IsStatic,
            IsPartial: isPartial,
            ContainingTypes: BuildContainingTypes(mapperSymbol),
            HintName: mapperSymbol.Name,
            Options: options,
            Methods: mappingMethods.ToEquatableArray(),
            UserMappings: userMappings.ToEquatableArray(),
            DeclaredMethodNames: declaredMethodNames.ToEquatableArray(),
            StructuralDiagnostics: structuralDiagnostics.ToEquatableArray(),
            Location: mapperLocation);
    }

    // ===================== mapper-level helpers =====================

    /// <summary>True only when every declared part of the type says "partial".</summary>
    private static bool IsDeclaredPartial(INamedTypeSymbol type)
    {
        if (type.DeclaringSyntaxReferences.Length == 0)
        {
            return false;
        }

        foreach (SyntaxReference reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not TypeDeclarationSyntax declaration ||
                !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Records the chain of outer types a nested mapper sits inside.</summary>
    private static EquatableArray<ContainingType> BuildContainingTypes(INamedTypeSymbol mapper)
    {
        var chain = new List<ContainingType>();
        for (INamedTypeSymbol? outer = mapper.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            string keyword = outer.TypeKind == TypeKind.Struct ? "struct" : "class";
            chain.Add(new ContainingType(keyword, outer.Name));
        }

        // The loop collected inner-most first; the emitter needs outer-most first.
        chain.Reverse();
        return chain.ToEquatableArray();
    }

    /// <summary>Reads the [Mapper] attribute into a settings record.</summary>
    private static MapperOptions ReadMapperOptions(AttributeData? attribute)
    {
        if (attribute is null)
        {
            return MapperOptions.Default;
        }

        return new MapperOptions(
            RequireExplicitMapping: BoolArgument(attribute, "RequireExplicitMapping", false),
            NameMatching: EnumArgument(attribute, "NameMatching", PropertyNameMatching.Exact),
            EnableFlattening: BoolArgument(attribute, "EnableFlattening", true),
            UnmappedTargetMember: EnumArgument(attribute, "UnmappedTargetMember", MemberMappingSeverity.Warn),
            UnmappedSourceMember: EnumArgument(attribute, "UnmappedSourceMember", MemberMappingSeverity.Ignore),
            EnumNameMatching: EnumArgument(attribute, "EnumNameMatching", PropertyNameMatching.Exact),
            NullPathHandling: EnumArgument(attribute, "NullPathHandling", NullHandling.Propagate),
            EnableStringConversions: BoolArgument(attribute, "EnableStringConversions", false));
    }

    // ===================== method-level helpers =====================

    /// <summary>Builds the model for one partial mapping method.</summary>
    private static MappingMethodModel BuildMappingMethod(
        IMethodSymbol method, Compilation compilation, CancellationToken ct)
    {
        // A void method with two parameters is an existing-instance update (7.8):
        // the first parameter is the source, the second is the target to fill.
        bool isUpdate = method.ReturnsVoid && method.Parameters.Length == 2;

        // A method that returns Task<T>/ValueTask<T> is an async mapping (7.11).
        // The real target type is the T inside the wrapper.
        bool isAsync = IsTaskLike(method.ReturnType, out ITypeSymbol? asyncResult, out bool returnsValueTask);

        IParameterSymbol sourceParam = method.Parameters[0];
        IParameterSymbol? targetParam = isUpdate ? method.Parameters[1] : null;

        // The target type is the second parameter for an update, the T inside
        // Task<T> for an async method, and the plain return type otherwise.
        ITypeSymbol targetTypeSymbol =
            isUpdate ? targetParam!.Type
            : isAsync ? asyncResult!
            : method.ReturnType;

        // An async method may carry a trailing CancellationToken parameter. If
        // it does, remember its name so the generated body can pass it along.
        string cancellationTokenName =
            isAsync && method.Parameters.Length == 2 &&
            IsCancellationToken(method.Parameters[1].Type)
                ? method.Parameters[1].Name
                : string.Empty;

        // Read any [MapDerivedType] entries. If the method has them it is a
        // polymorphic mapping, and we also discover every concrete subtype of
        // its source type so Stage 3 can check the coverage is complete.
        List<DerivedTypeMapping> derivedTypes = ReadDerivedTypes(method);
        EquatableArray<DerivedTypeRef> discovered = derivedTypes.Count == 0
            ? EquatableArray<DerivedTypeRef>.Empty
            : DiscoverDerivedTypes(sourceParam.Type, compilation, ct).ToEquatableArray();

        return new MappingMethodModel(
            MethodName: method.Name,
            Accessibility: AccessibilityText(method.DeclaredAccessibility),
            IsStatic: method.IsStatic,
            IsPartialNoBody: true,
            SourceParameterName: sourceParam.Name,
            SourceType: BuildType(sourceParam.Type, 0, new HashSet<string>(), ct),
            TargetType: BuildType(targetTypeSymbol, 0, new HashSet<string>(), ct),
            SourceParameterTypeDisplay: sourceParam.Type.ToDisplayString(FullyQualifiedWithNullable),
            ReturnTypeDisplay: isUpdate
                ? "void"
                : method.ReturnType.ToDisplayString(FullyQualifiedWithNullable),
            Configurations: ReadMemberConfigs(method).ToEquatableArray(),
            Location: LocationInfo.CreateFrom(method.Locations.FirstOrDefault() ?? Location.None),
            DerivedTypes: derivedTypes.ToEquatableArray(),
            DiscoveredDerivedTypes: discovered,
            IsUpdate: isUpdate,
            TargetParameterName: isUpdate ? targetParam!.Name : string.Empty,
            IsAsyncMethod: isAsync,
            ReturnsValueTask: returnsValueTask,
            CancellationTokenName: cancellationTokenName);
    }

    /// <summary>Builds the model for one user-written conversion method.</summary>
    private static UserMappingModel BuildUserMapping(IMethodSymbol method)
    {
        IParameterSymbol parameter = method.Parameters[0];
        AttributeData? userMappingAttr = FindAttribute(method, UserMappingAttribute);

        // A user method that returns Task<T>/ValueTask<T> is an async conversion.
        // The type callers really convert TO is the T inside the wrapper, so we
        // record that as the return type and flag the method as async (7.11).
        bool isAsync = IsTaskLike(method.ReturnType, out ITypeSymbol? asyncResult, out _);
        ITypeSymbol returnType = isAsync ? asyncResult! : method.ReturnType;

        return new UserMappingModel(
            MethodName: method.Name,
            IsStatic: method.IsStatic,
            ParameterTypeKey: parameter.Type.ToDisplayString(FullyQualified),
            ParameterTypeDisplay: parameter.Type.ToDisplayString(FullyQualified),
            ReturnTypeKey: returnType.ToDisplayString(FullyQualified),
            ReturnTypeDisplay: returnType.ToDisplayString(FullyQualified),
            IsExplicitlyMarked: userMappingAttr is not null,
            IsIgnored: userMappingAttr is not null && BoolArgument(userMappingAttr, "Ignore", false),
            Location: LocationInfo.CreateFrom(method.Locations.FirstOrDefault() ?? Location.None),
            IsAsync: isAsync);
    }

    /// <summary>
    /// If the property is a partial Expression&lt;Func&lt;TSource, TTarget&gt;&gt;
    /// projection the generator should fill in, builds its model. Returns null
    /// for any other property (design doc 7.10).
    /// </summary>
    private static MappingMethodModel? TryBuildProjection(IPropertySymbol property, CancellationToken ct)
    {
        // It must be the defining half of a partial property with no
        // implementation supplied yet - that is exactly what the generator
        // fills in. This is read from the syntax: "a partial property with no
        // body anywhere". The syntax model is the same on every Roslyn version,
        // so one generator build works in every compiler.
        if (property.DeclaringSyntaxReferences.Length == 0)
        {
            return null;
        }

        foreach (SyntaxReference reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(ct) is not PropertyDeclarationSyntax declaration ||
                !declaration.Modifiers.Any(SyntaxKind.PartialKeyword) ||
                PropertyHasBody(declaration))
            {
                // Not partial, or some part already has an implementation:
                // there is nothing for the generator to do.
                return null;
            }
        }

        // The type must be Expression<Func<TSource, TTarget>>.
        if (!TryReadProjectionTypes(property.Type, out ITypeSymbol? sourceType, out ITypeSymbol? targetType))
        {
            return null;
        }

        return new MappingMethodModel(
            MethodName: property.Name,
            Accessibility: AccessibilityText(property.DeclaredAccessibility),
            IsStatic: property.IsStatic,
            IsPartialNoBody: true,
            // The lambda parameter is named after the source type ("Order" -> "order").
            SourceParameterName: LambdaParameterName(sourceType!),
            SourceType: BuildType(sourceType!, 0, new HashSet<string>(), ct),
            TargetType: BuildType(targetType!, 0, new HashSet<string>(), ct),
            SourceParameterTypeDisplay: sourceType!.ToDisplayString(FullyQualified),
            ReturnTypeDisplay: property.Type.ToDisplayString(FullyQualifiedWithNullable),
            Configurations: ReadMemberConfigs(property).ToEquatableArray(),
            Location: LocationInfo.CreateFrom(property.Locations.FirstOrDefault() ?? Location.None),
            IsProjection: true);
    }

    /// <summary>True when a property declaration already supplies an implementation.</summary>
    private static bool PropertyHasBody(PropertyDeclarationSyntax declaration)
    {
        // An expression-bodied property: "Name => ...".
        if (declaration.ExpressionBody is not null)
        {
            return true;
        }

        // Or any accessor that has a body or an expression body.
        if (declaration.AccessorList is not null)
        {
            foreach (AccessorDeclarationSyntax accessor in declaration.AccessorList.Accessors)
            {
                if (accessor.Body is not null || accessor.ExpressionBody is not null)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Reads Expression&lt;Func&lt;TSource, TTarget&gt;&gt; into its source and
    /// target types. Returns false for any other property type.
    /// </summary>
    private static bool TryReadProjectionTypes(
        ITypeSymbol propertyType, out ITypeSymbol? sourceType, out ITypeSymbol? targetType)
    {
        sourceType = null;
        targetType = null;

        // Expression<TDelegate> - exactly one type argument.
        if (propertyType is not INamedTypeSymbol expression ||
            expression.Name != "Expression" ||
            (expression.ContainingNamespace?.ToDisplayString() ?? string.Empty)
                != "System.Linq.Expressions" ||
            expression.TypeArguments.Length != 1)
        {
            return false;
        }

        // The delegate must be Func<TSource, TTarget> - exactly two type arguments.
        if (expression.TypeArguments[0] is not INamedTypeSymbol func ||
            func.Name != "Func" ||
            (func.ContainingNamespace?.ToDisplayString() ?? string.Empty) != "System" ||
            func.TypeArguments.Length != 2)
        {
            return false;
        }

        sourceType = func.TypeArguments[0];
        targetType = func.TypeArguments[1];
        return true;
    }

    /// <summary>Picks a readable lambda parameter name from the source type name.</summary>
    private static string LambdaParameterName(ITypeSymbol sourceType)
    {
        string name = sourceType.Name;
        if (name.Length == 0)
        {
            return "source";
        }

        // Lower-case the first letter: "Order" -> "order".
        string candidate = char.ToLowerInvariant(name[0]) + name.Substring(1);

        // Prefix with "@" if the result happens to be a C# keyword.
        return SyntaxFacts.GetKeywordKind(candidate) == SyntaxKind.None ? candidate : "@" + candidate;
    }

    /// <summary>Reads every [MapProperty] / [MapValue] / [MapperIgnore*] on a method or property.</summary>
    private static List<MemberConfig> ReadMemberConfigs(ISymbol method)
    {
        var configs = new List<MemberConfig>();

        foreach (AttributeData attribute in method.GetAttributes())
        {
            string? name = attribute.AttributeClass?.ToDisplayString(PlainName);
            var location = LocationInfo.CreateFrom(GetAttributeLocation(attribute));

            switch (name)
            {
                case MapPropertyAttribute:
                    configs.Add(new MemberConfig(
                        Kind: ConfigKind.MapProperty,
                        Source: ConstructorString(attribute, 0),
                        Target: ConstructorString(attribute, 1),
                        Use: NamedString(attribute, "Use"),
                        ConstantExpression: null,
                        HasConstant: false,
                        Location: location));
                    break;

                case MapperIgnoreTargetAttribute:
                    configs.Add(new MemberConfig(
                        ConfigKind.IgnoreTarget, null, ConstructorString(attribute, 0),
                        null, null, false, location));
                    break;

                case MapperIgnoreSourceAttribute:
                    configs.Add(new MemberConfig(
                        ConfigKind.IgnoreSource, ConstructorString(attribute, 0), null,
                        null, null, false, location));
                    break;

                case MapValueAttribute:
                    (string? constant, bool hasConstant) = ReadMapValueConstant(attribute);
                    configs.Add(new MemberConfig(
                        Kind: ConfigKind.MapValue,
                        Source: null,
                        Target: ConstructorString(attribute, 0),
                        Use: NamedString(attribute, "Use"),
                        ConstantExpression: constant,
                        HasConstant: hasConstant,
                        Location: location));
                    break;
            }
        }

        return configs;
    }

    /// <summary>Reads the constant from a [MapValue(Value = ...)], if there is one.</summary>
    private static (string? Constant, bool HasConstant) ReadMapValueConstant(AttributeData attribute)
    {
        foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
        {
            if (named.Key == "Value")
            {
                return (RenderConstant(named.Value), true);
            }
        }

        return (null, false);
    }

    // ===================== type-graph helpers =====================

    /// <summary>
    /// Builds the value-comparable snapshot of a type. Recurses into the members
    /// of complex types, but stops at simple types, at a depth limit, and at any
    /// type that would loop back on itself.
    /// </summary>
    private static TypeModel BuildType(ITypeSymbol type, int depth, HashSet<string> visiting, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        string display = type.ToDisplayString(FullyQualified);
        string key = display;
        bool isReference = type.IsReferenceType;

        // An error type means the user's code does not compile yet. Treat it as
        // an unknown leaf so we do not crash before the real compiler error.
        if (type.TypeKind == TypeKind.Error)
        {
            return TypeModel.Leaf(display, key, TypeCategory.Unknown, isReference);
        }

        // Nullable<T> (a value type such as int?).
        if (type is INamedTypeSymbol nullable &&
            nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            TypeModel inner = BuildType(nullable.TypeArguments[0], depth, visiting, ct);
            return new TypeModel(
                display, key, TypeCategory.Nullable, false, true, inner,
                EquatableArray<MemberModel>.Empty, EquatableArray<ConstructorModel>.Empty,
                EquatableArray<string>.Empty);
        }

        // An enum: remember its member names so we can map enum to enum by name.
        if (type.TypeKind == TypeKind.Enum)
        {
            var enumNames = type.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(f => f.IsConst)
                .Select(f => f.Name)
                .ToEquatableArray();
            return TypeModel.Leaf(display, key, TypeCategory.Enum, false, enumMembers: enumNames);
        }

        // A simple value that is copied straight across.
        if (IsSimpleType(type))
        {
            return TypeModel.Leaf(display, key, TypeCategory.Simple, isReference);
        }

        // A collection - array, list, set, dictionary, or immutable collection.
        if (TryGetCollectionInfo(type, out CollectionKind collectionKind,
                out ITypeSymbol? elementType, out ITypeSymbol? keyType))
        {
            TypeModel? element = elementType is null
                ? null
                : BuildType(elementType, depth + 1, visiting, ct);
            TypeModel? keyModel = keyType is null
                ? null
                : BuildType(keyType, depth + 1, visiting, ct);
            return new TypeModel(
                display, key, TypeCategory.Collection, isReference, type.IsValueType, element,
                EquatableArray<MemberModel>.Empty, EquatableArray<ConstructorModel>.Empty,
                EquatableArray<string>.Empty, collectionKind, keyModel);
        }

        // A class, struct, or record we can look inside and map member by member.
        if (type is INamedTypeSymbol named &&
            (type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Struct))
        {
            // Stop if we are too deep, or if this type is already on the path
            // above us (a reference cycle). Return it without its members.
            if (depth >= MaxTypeDepth || visiting.Contains(key))
            {
                return new TypeModel(
                    display, key, TypeCategory.Complex, isReference, type.IsValueType, null,
                    EquatableArray<MemberModel>.Empty, EquatableArray<ConstructorModel>.Empty,
                    EquatableArray<string>.Empty);
            }

            visiting.Add(key);
            EquatableArray<MemberModel> members = GatherMembers(named, depth, visiting, ct);
            EquatableArray<ConstructorModel> constructors = GatherConstructors(named, depth, visiting, ct);
            visiting.Remove(key);

            return new TypeModel(
                display, key, TypeCategory.Complex, isReference, type.IsValueType, null,
                members, constructors, EquatableArray<string>.Empty);
        }

        // Anything else (an interface, a pointer, ...) is treated as unknown.
        return TypeModel.Leaf(display, key, TypeCategory.Unknown, isReference);
    }

    /// <summary>Collects the readable/writable properties and fields of a type, including inherited ones.</summary>
    private static EquatableArray<MemberModel> GatherMembers(
        INamedTypeSymbol type, int depth, HashSet<string> visiting, CancellationToken ct)
    {
        var members = new List<MemberModel>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        // Walk from the type up through its base types so inherited members count.
        for (INamedTypeSymbol? current = type;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType)
        {
            foreach (ISymbol symbol in current.GetMembers())
            {
                ct.ThrowIfCancellationRequested();

                if (symbol.IsStatic || !seenNames.Add(symbol.Name))
                {
                    continue;
                }

                if (symbol is IPropertySymbol property && !property.IsIndexer &&
                    property.Name != "EqualityContract")
                {
                    members.Add(BuildPropertyMember(property, depth, visiting, ct));
                }
                else if (symbol is IFieldSymbol field && !field.IsConst && !field.IsImplicitlyDeclared)
                {
                    members.Add(BuildFieldMember(field, depth, visiting, ct));
                }
            }
        }

        return members.ToEquatableArray();
    }

    private static MemberModel BuildPropertyMember(
        IPropertySymbol property, int depth, HashSet<string> visiting, CancellationToken ct)
    {
        bool canRead = property.GetMethod is not null && IsAccessible(property.GetMethod.DeclaredAccessibility);
        bool canWrite = property.SetMethod is not null && IsAccessible(property.SetMethod.DeclaredAccessibility);

        return new MemberModel(
            Name: property.Name,
            Type: BuildType(property.Type, depth + 1, visiting, ct),
            IsNullableReference: IsNullableReference(property.Type),
            CanRead: canRead,
            CanWrite: canWrite,
            IsInitOnly: property.SetMethod?.IsInitOnly == true,
            IsRequired: property.IsRequired,
            IsField: false);
    }

    private static MemberModel BuildFieldMember(
        IFieldSymbol field, int depth, HashSet<string> visiting, CancellationToken ct)
    {
        bool accessible = IsAccessible(field.DeclaredAccessibility);

        return new MemberModel(
            Name: field.Name,
            Type: BuildType(field.Type, depth + 1, visiting, ct),
            IsNullableReference: IsNullableReference(field.Type),
            CanRead: accessible,
            CanWrite: accessible && !field.IsReadOnly,
            IsInitOnly: false,
            IsRequired: field.IsRequired,
            IsField: true);
    }

    /// <summary>Collects the constructors Synthetix is allowed to call.</summary>
    private static EquatableArray<ConstructorModel> GatherConstructors(
        INamedTypeSymbol type, int depth, HashSet<string> visiting, CancellationToken ct)
    {
        var constructors = new List<ConstructorModel>();

        foreach (IMethodSymbol constructor in type.InstanceConstructors)
        {
            ct.ThrowIfCancellationRequested();

            var parameters = constructor.Parameters
                .Select(p => new ParameterModel(
                    Name: p.Name,
                    Type: BuildType(p.Type, depth + 1, visiting, ct),
                    IsNullableReference: IsNullableReference(p.Type),
                    IsOptional: p.IsOptional))
                .ToEquatableArray();

            constructors.Add(new ConstructorModel(parameters, IsAccessible(constructor.DeclaredAccessibility)));
        }

        return constructors.ToEquatableArray();
    }

    /// <summary>True for the built-in simple values that are just copied across.</summary>
    private static bool IsSimpleType(ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
            case SpecialType.System_Char:
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Decimal:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_String:
            case SpecialType.System_Object:
            case SpecialType.System_DateTime:
                return true;
        }

        // A few more well-known value types that behave like simple values.
        switch (type.ToDisplayString(PlainName))
        {
            case "System.Guid":
            case "System.DateTimeOffset":
            case "System.TimeSpan":
            case "System.DateOnly":
            case "System.TimeOnly":
            case "System.Uri":
            case "System.Version":
                return true;
        }

        return false;
    }

    /// <summary>
    /// Detects whether a type is a collection Synthetix understands, and if so
    /// reports its family, element type, and (for a dictionary) key type.
    /// </summary>
    private static bool TryGetCollectionInfo(
        ITypeSymbol type, out CollectionKind kind, out ITypeSymbol? element, out ITypeSymbol? key)
    {
        kind = CollectionKind.None;
        element = null;
        key = null;

        // A single-dimension array.
        if (type is IArrayTypeSymbol array && array.Rank == 1)
        {
            kind = CollectionKind.Array;
            element = array.ElementType;
            return true;
        }

        if (type is not INamedTypeSymbol named || !named.IsGenericType)
        {
            return false;
        }

        kind = ClassifyCollection(named);
        if (kind == CollectionKind.None)
        {
            return false;
        }

        var arguments = named.TypeArguments;
        if (kind == CollectionKind.Dictionary || kind == CollectionKind.ImmutableDictionary)
        {
            if (arguments.Length != 2)
            {
                kind = CollectionKind.None;
                return false;
            }

            key = arguments[0];
            element = arguments[1];
        }
        else
        {
            if (arguments.Length != 1)
            {
                kind = CollectionKind.None;
                return false;
            }

            element = arguments[0];
        }

        return true;
    }

    /// <summary>Maps a generic type onto a collection family by its namespace and name.</summary>
    private static CollectionKind ClassifyCollection(INamedTypeSymbol named)
    {
        string @namespace = named.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        if (@namespace == "System.Collections.Generic")
        {
            switch (named.Name)
            {
                case "List":
                case "IList":
                case "ICollection":
                case "IEnumerable":
                case "IReadOnlyList":
                case "IReadOnlyCollection":
                    return CollectionKind.List;
                case "HashSet":
                case "ISet":
                case "IReadOnlySet":
                    return CollectionKind.Set;
                case "Dictionary":
                case "IDictionary":
                case "IReadOnlyDictionary":
                    return CollectionKind.Dictionary;
            }
        }
        else if (@namespace == "System.Collections.Immutable")
        {
            switch (named.Name)
            {
                case "ImmutableArray":
                    return CollectionKind.ImmutableArray;
                case "ImmutableList":
                case "IImmutableList":
                    return CollectionKind.ImmutableList;
                case "ImmutableHashSet":
                case "IImmutableSet":
                    return CollectionKind.ImmutableHashSet;
                case "ImmutableDictionary":
                case "IImmutableDictionary":
                    return CollectionKind.ImmutableDictionary;
            }
        }

        return CollectionKind.None;
    }

    /// <summary>Reads every [MapDerivedType] entry on a mapping method.</summary>
    private static List<DerivedTypeMapping> ReadDerivedTypes(IMethodSymbol method)
    {
        var result = new List<DerivedTypeMapping>();

        foreach (AttributeData attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString(PlainName) != "Synthetix.MapDerivedTypeAttribute" ||
                attribute.ConstructorArguments.Length != 2)
            {
                continue;
            }

            if (attribute.ConstructorArguments[0].Value is not ITypeSymbol sourceType ||
                attribute.ConstructorArguments[1].Value is not ITypeSymbol targetType)
            {
                continue;
            }

            string sourceDisplay = sourceType.ToDisplayString(FullyQualified);
            string targetDisplay = targetType.ToDisplayString(FullyQualified);
            result.Add(new DerivedTypeMapping(
                sourceDisplay, sourceDisplay, targetDisplay, targetDisplay,
                LocationInfo.CreateFrom(GetAttributeLocation(attribute))));
        }

        return result;
    }

    /// <summary>
    /// Finds every concrete class in the current compilation that derives from
    /// the given base type. Used for the SYNTX020 exhaustiveness check.
    /// </summary>
    private static List<DerivedTypeRef> DiscoverDerivedTypes(
        ITypeSymbol baseType, Compilation compilation, CancellationToken ct)
    {
        var result = new List<DerivedTypeRef>();

        foreach (INamedTypeSymbol type in EnumerateTypes(compilation.Assembly.GlobalNamespace, ct))
        {
            if (type.IsAbstract || type.TypeKind != TypeKind.Class)
            {
                continue;
            }

            if (InheritsFrom(type, baseType))
            {
                string display = type.ToDisplayString(FullyQualified);
                result.Add(new DerivedTypeRef(display, display));
            }
        }

        return result;
    }

    /// <summary>Walks every type declared in a namespace tree, nested types included.</summary>
    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol @namespace, CancellationToken ct)
    {
        foreach (ISymbol member in @namespace.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            if (member is INamespaceSymbol childNamespace)
            {
                foreach (INamedTypeSymbol type in EnumerateTypes(childNamespace, ct))
                {
                    yield return type;
                }
            }
            else if (member is INamedTypeSymbol declaredType)
            {
                foreach (INamedTypeSymbol type in EnumerateTypeTree(declaredType))
                {
                    yield return type;
                }
            }
        }
    }

    /// <summary>Yields a type and, recursively, every type nested inside it.</summary>
    private static IEnumerable<INamedTypeSymbol> EnumerateTypeTree(INamedTypeSymbol type)
    {
        yield return type;

        foreach (INamedTypeSymbol nested in type.GetTypeMembers())
        {
            foreach (INamedTypeSymbol deeper in EnumerateTypeTree(nested))
            {
                yield return deeper;
            }
        }
    }

    /// <summary>True when a type has the given base type somewhere up its inheritance chain.</summary>
    private static bool InheritsFrom(INamedTypeSymbol type, ITypeSymbol baseType)
    {
        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    // ===================== small shared helpers =====================

    /// <summary>True when a reference-type member is marked nullable ("string?").</summary>
    private static bool IsNullableReference(ITypeSymbol type)
        => type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated;

    /// <summary>
    /// Detects whether a type is Task&lt;T&gt; or ValueTask&lt;T&gt;. When it is,
    /// reports the inner result type T and whether the wrapper is a ValueTask.
    /// </summary>
    private static bool IsTaskLike(ITypeSymbol type, out ITypeSymbol? resultType, out bool isValueTask)
    {
        resultType = null;
        isValueTask = false;

        // Both wrappers are generic types with exactly one type argument.
        if (type is not INamedTypeSymbol named || !named.IsGenericType ||
            named.TypeArguments.Length != 1)
        {
            return false;
        }

        if ((named.ContainingNamespace?.ToDisplayString() ?? string.Empty)
            != "System.Threading.Tasks")
        {
            return false;
        }

        if (named.Name == "Task")
        {
            resultType = named.TypeArguments[0];
            return true;
        }

        if (named.Name == "ValueTask")
        {
            resultType = named.TypeArguments[0];
            isValueTask = true;
            return true;
        }

        return false;
    }

    /// <summary>True when a type is System.Threading.CancellationToken.</summary>
    private static bool IsCancellationToken(ITypeSymbol type)
        => type.Name == "CancellationToken" &&
           (type.ContainingNamespace?.ToDisplayString() ?? string.Empty) == "System.Threading";

    /// <summary>We treat public and internal members as usable from the generated code.</summary>
    private static bool IsAccessible(Accessibility accessibility)
        => accessibility == Accessibility.Public || accessibility == Accessibility.Internal;

    /// <summary>Turns an accessibility value into the C# keyword(s) for it.</summary>
    private static string AccessibilityText(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Private => "private",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => "internal",
    };

    private static bool HasAnyConfigAttribute(IMethodSymbol method)
    {
        foreach (AttributeData attribute in method.GetAttributes())
        {
            switch (attribute.AttributeClass?.ToDisplayString(PlainName))
            {
                case MapPropertyAttribute:
                case MapperIgnoreTargetAttribute:
                case MapperIgnoreSourceAttribute:
                case MapValueAttribute:
                    return true;
            }
        }

        return false;
    }

    private static AttributeData? FindAttribute(IMethodSymbol method, string metadataName)
    {
        foreach (AttributeData attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString(PlainName) == metadataName)
            {
                return attribute;
            }
        }

        return null;
    }

    private static Location GetAttributeLocation(AttributeData attribute)
        => attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None;

    private static string? ConstructorString(AttributeData attribute, int index)
        => attribute.ConstructorArguments.Length > index
            ? attribute.ConstructorArguments[index].Value as string
            : null;

    private static string? NamedString(AttributeData attribute, string name)
    {
        foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
        {
            if (named.Key == name)
            {
                return named.Value.Value as string;
            }
        }

        return null;
    }

    private static bool BoolArgument(AttributeData attribute, string name, bool fallback)
    {
        foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
        {
            if (named.Key == name && named.Value.Value is bool value)
            {
                return value;
            }
        }

        return fallback;
    }

    private static T EnumArgument<T>(AttributeData attribute, string name, T fallback)
        where T : struct, Enum
    {
        foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
        {
            if (named.Key == name && named.Value.Value is not null)
            {
                return (T)Enum.ToObject(typeof(T), named.Value.Value);
            }
        }

        return fallback;
    }

    /// <summary>Turns an attribute constant value into the C# text that reproduces it.</summary>
    private static string RenderConstant(TypedConstant constant)
    {
        if (constant.IsNull)
        {
            return "null";
        }

        // An enum value: write it as EnumType.Member when we can find the name.
        if (constant.Kind == TypedConstantKind.Enum && constant.Type is INamedTypeSymbol enumType)
        {
            foreach (IFieldSymbol field in enumType.GetMembers().OfType<IFieldSymbol>())
            {
                if (field.IsConst && Equals(field.ConstantValue, constant.Value))
                {
                    return enumType.ToDisplayString(FullyQualified) + "." + field.Name;
                }
            }

            return "(" + enumType.ToDisplayString(FullyQualified) + ")" +
                   Convert.ToString(constant.Value, CultureInfo.InvariantCulture);
        }

        if (constant.Kind == TypedConstantKind.Type && constant.Value is ITypeSymbol typeValue)
        {
            return "typeof(" + typeValue.ToDisplayString(FullyQualified) + ")";
        }

        // A plain primitive value.
        return constant.Value switch
        {
            string text => SymbolDisplay.FormatLiteral(text, quote: true),
            char character => SymbolDisplay.FormatLiteral(character, quote: true),
            bool flag => flag ? "true" : "false",
            float number => number.ToString(CultureInfo.InvariantCulture) + "f",
            double number => number.ToString(CultureInfo.InvariantCulture) + "d",
            decimal number => number.ToString(CultureInfo.InvariantCulture) + "m",
            long number => number.ToString(CultureInfo.InvariantCulture) + "L",
            uint number => number.ToString(CultureInfo.InvariantCulture) + "u",
            ulong number => number.ToString(CultureInfo.InvariantCulture) + "ul",
            null => "null",
            _ => Convert.ToString(constant.Value, CultureInfo.InvariantCulture) ?? "null",
        };
    }
}
