# SYNTX009: Circular reference detected

**Severity:** Error

The type graph being mapped loops back on itself and there is no configured way to handle the cycle.

## Cause

A type reaches itself through its members (directly or indirectly) and the mapping would recurse forever.

## How to fix it

Break the cycle by mapping the recursive member through a dedicated sibling mapping method, or by ignoring it with `[MapperIgnoreTarget]`.
