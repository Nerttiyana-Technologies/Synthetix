# SYNTX019: Unsupported collection type

**Severity:** Error

The target collection type is one Synthetix does not know how to construct.

## Cause

The target member is a collection family the generator cannot build - Synthetix supports arrays, the `List`/`Set`/`Dictionary` interfaces and classes, and the immutable collections.

## How to fix it

Use a supported collection type for the target member, or supply a `Use` converter or user mapping that builds the collection yourself.
