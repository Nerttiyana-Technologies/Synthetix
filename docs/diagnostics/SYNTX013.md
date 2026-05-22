# SYNTX013: Explicit mapping required

**Severity:** Error

`RequireExplicitMapping` is enabled on the mapper, which means every target member must be deliberately mapped or deliberately ignored.

## Cause

A target member is neither mapped nor ignored, and the mapper has `[Mapper(RequireExplicitMapping = true)]`.

## How to fix it

Map the member with `[MapProperty]` or `[MapValue]`, or mark it `[MapperIgnoreTarget]`.
