# SYNTX012: Nullable mapped to non-nullable

**Severity:** Warning

A nullable source value is being assigned to a target member that cannot be null. The generated code still compiles, but it can throw or produce a default value at runtime if the source is null.

## Cause

A nullable source member, or a nullable step on a flatten path, maps to a non-nullable target member.

## How to fix it

Make the target member nullable, or accept the runtime behaviour. For a flatten path you can also set `[Mapper(NullPathHandling = ...)]` to choose between propagating, throwing, or using a default value.
