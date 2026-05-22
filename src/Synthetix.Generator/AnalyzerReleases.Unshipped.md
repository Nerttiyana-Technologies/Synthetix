; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SYNTX001 | Synthetix | Error | Mapper class must be declared partial.
SYNTX002 | Synthetix | Error | Mapping method must be declared partial and have no body.
SYNTX003 | Synthetix | Error | No mapping could be created from the source type to the target type.
SYNTX004 | Synthetix | Warning | Target member has no corresponding source member.
SYNTX005 | Synthetix | Info | Source member is not used by any mapping.
SYNTX006 | Synthetix | Error | Configuration references a target member that does not exist.
SYNTX007 | Synthetix | Error | Configuration references a source path that cannot be resolved.
SYNTX008 | Synthetix | Error | Ambiguous flattening between multiple source paths.
SYNTX009 | Synthetix | Error | Circular reference detected with no configured handling.
SYNTX010 | Synthetix | Error | Member type mismatch with no conversion available.
SYNTX011 | Synthetix | Error | Target type has no constructor Synthetix can satisfy.
SYNTX012 | Synthetix | Warning | Nullable source member mapped to a non-nullable target member.
SYNTX013 | Synthetix | Error | RequireExplicitMapping is on but a target member is unaccounted for.
SYNTX014 | Synthetix | Error | Conflicting configuration at equal precedence on one member.
SYNTX015 | Synthetix | Error | Referenced Use or user-mapping method was not found.
SYNTX016 | Synthetix | Error | Mapper class declares no partial mapping methods.
SYNTX017 | Synthetix | Warning | Mapping has drifted from the committed manifest.
SYNTX018 | Synthetix | Error | Collection element type cannot be mapped.
SYNTX019 | Synthetix | Error | Unsupported collection type that Synthetix cannot construct.
SYNTX020 | Synthetix | Error | Polymorphic mapping is not exhaustive over its derived types.
SYNTX021 | Synthetix | Error | A [MapDerivedType] entry is invalid or has no mapping.
SYNTX022 | Synthetix | Warning | A member cannot be updated on an existing instance.
SYNTX023 | Synthetix | Error | A mapping cannot be expressed as an IQueryable projection.
SYNTX024 | Synthetix | Error | An async user mapping is required by a synchronous method.
