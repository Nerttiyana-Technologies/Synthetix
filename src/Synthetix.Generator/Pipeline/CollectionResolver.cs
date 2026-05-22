namespace Synthetix.Pipeline;

using System.Collections.Generic;
using Synthetix.Diagnostics;
using Synthetix.Models;

/// <summary>
/// Collects the collection helper methods a mapper needs. Each distinct
/// collection conversion becomes one private helper method, and identical
/// conversions are shared.
/// </summary>
internal sealed class CollectionHelperRegistry
{
    /// <summary>Every helper to emit, in the order they were registered.</summary>
    public List<CollectionPlan> Plans { get; } = new();

    /// <summary>Maps a "source =&gt; target" key to the helper method already made for it.</summary>
    public Dictionary<string, string> NamesByConversion { get; } = new();
}

/// <summary>
/// Stage 3 helper for collection-element mapping (design doc 7.6). It works out
/// how to convert one collection into another and records a helper method that
/// the emitter will write.
/// </summary>
internal static class CollectionResolver
{
    /// <summary>
    /// Resolves a collection-to-collection conversion. Returns the C# expression
    /// that produces the target collection, or null if it cannot be done (a
    /// diagnostic is added in that case).
    /// </summary>
    public static string? Resolve(
        TypeModel sourceCollection,
        TypeModel targetCollection,
        string sourceExpression,
        MapperModel mapper,
        CollectionHelperRegistry registry,
        List<DiagnosticInfo> diagnostics,
        LocationInfo? location,
        string memberLabel)
    {
        // Fast path: two identical collection types need no per-element work.
        if (sourceCollection.IdentityKey == targetCollection.IdentityKey)
        {
            return sourceExpression;
        }

        // Re-use a helper if this exact conversion was already needed.
        string conversionKey = sourceCollection.IdentityKey + " => " + targetCollection.IdentityKey;
        if (registry.NamesByConversion.TryGetValue(conversionKey, out string? existing))
        {
            return existing + "(" + sourceExpression + ")";
        }

        // The target has to be a collection family the generator can construct.
        if (targetCollection.CollectionKind == CollectionKind.None || targetCollection.Inner is null)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.UnsupportedCollectionType, location, targetCollection.DisplayName));
            return null;
        }

        bool isDictionary = targetCollection.IsDictionary;
        string? keyConversion = null;
        string? elementConversion;

        if (isDictionary)
        {
            if (sourceCollection.Inner is null || sourceCollection.KeyType is null ||
                targetCollection.KeyType is null)
            {
                ReportElementFailure(diagnostics, location, memberLabel, sourceCollection, targetCollection);
                return null;
            }

            keyConversion = ResolveElement(
                sourceCollection.KeyType, targetCollection.KeyType, "pair.Key",
                mapper, registry, diagnostics, location);
            elementConversion = ResolveElement(
                sourceCollection.Inner, targetCollection.Inner, "pair.Value",
                mapper, registry, diagnostics, location);
        }
        else
        {
            if (sourceCollection.Inner is null)
            {
                ReportElementFailure(diagnostics, location, memberLabel, sourceCollection, targetCollection);
                return null;
            }

            elementConversion = ResolveElement(
                sourceCollection.Inner, targetCollection.Inner, "element",
                mapper, registry, diagnostics, location);
        }

        if (elementConversion is null || (isDictionary && keyConversion is null))
        {
            ReportElementFailure(diagnostics, location, memberLabel, sourceCollection, targetCollection);
            return null;
        }

        // Register the helper. Any nested helpers were already added above, so
        // the index here is unique and stable.
        string name = "__MapCollection" + registry.Plans.Count;
        registry.Plans.Add(new CollectionPlan(
            MethodName: name,
            SourceTypeDisplay: sourceCollection.DisplayName,
            TargetTypeDisplay: targetCollection.DisplayName,
            TargetKind: targetCollection.CollectionKind,
            ConcreteTargetTypeDisplay: ConcreteType(targetCollection),
            ElementTypeDisplay: targetCollection.Inner.DisplayName,
            IsDictionary: isDictionary,
            KeyTypeDisplay: targetCollection.KeyType?.DisplayName,
            ElementConversion: elementConversion,
            KeyConversion: keyConversion));
        registry.NamesByConversion[conversionKey] = name;

        return name + "(" + sourceExpression + ")";
    }

    /// <summary>
    /// Resolves the conversion of a single element. A collection element is sent
    /// back through <see cref="Resolve"/> (nested collections); anything else
    /// goes through the normal scalar conversion rules.
    /// </summary>
    private static string? ResolveElement(
        TypeModel source,
        TypeModel target,
        string valueExpression,
        MapperModel mapper,
        CollectionHelperRegistry registry,
        List<DiagnosticInfo> diagnostics,
        LocationInfo? location)
    {
        if (source.IsCollection && target.IsCollection)
        {
            return Resolve(source, target, valueExpression, mapper, registry, diagnostics, location, "element");
        }

        ConversionResult conversion = ConversionResolver.Convert(
            valueExpression,
            source,
            sourceCanBeNull: false,
            target,
            targetAllowsNull: true,
            mapper,
            location,
            "element",
            "element");

        // An element only stops the whole collection when it cannot be converted
        // at all; a mere warning is acceptable for an element.
        return conversion.Succeeded ? conversion.Expression : null;
    }

    /// <summary>Works out the concrete type the helper should construct.</summary>
    private static string ConcreteType(TypeModel targetCollection)
    {
        string element = targetCollection.Inner!.DisplayName;

        return targetCollection.CollectionKind switch
        {
            // An array is built as a List first, then turned into an array.
            CollectionKind.Array => "global::System.Collections.Generic.List<" + element + ">",
            CollectionKind.List => "global::System.Collections.Generic.List<" + element + ">",
            CollectionKind.Set => "global::System.Collections.Generic.HashSet<" + element + ">",
            CollectionKind.Dictionary =>
                "global::System.Collections.Generic.Dictionary<" +
                targetCollection.KeyType!.DisplayName + ", " + element + ">",

            // The immutable kinds are built with their builder, so no concrete
            // type is needed here - the emitter handles them by kind.
            _ => string.Empty,
        };
    }

    private static void ReportElementFailure(
        List<DiagnosticInfo> diagnostics,
        LocationInfo? location,
        string memberLabel,
        TypeModel source,
        TypeModel target)
    {
        diagnostics.Add(DiagnosticInfo.Create(
            DiagnosticDescriptors.CollectionElementNotMappable,
            location,
            memberLabel,
            (source.Inner ?? source).DisplayName,
            (target.Inner ?? target).DisplayName));
    }
}
