# SYNTX016: Mapper has no mapping methods

**Severity:** Error

A type is marked `[Mapper]` but declares no partial mapping methods, so the generator has nothing to do.

## Cause

A `[Mapper]` class or struct contains no partial mapping methods and no projection properties.

## How to fix it

Add at least one partial mapping method, or remove the `[Mapper]` attribute.
