# SYNTX021: Invalid derived-type mapping

**Severity:** Error

A `[MapDerivedType]` entry is invalid - usually because there is no mapping the generator can call for the derived source and target pair.

## Cause

A `[MapDerivedType]` names a derived pair, but no sibling mapping method or user mapping converts that source type to that target type.

## How to fix it

Add a mapping method for the derived pair, or correct the types named in the `[MapDerivedType]` attribute.
