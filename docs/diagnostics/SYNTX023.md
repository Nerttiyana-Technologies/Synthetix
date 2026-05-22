# SYNTX023: Mapping is not projectable

**Severity:** Error

A mapping declared as an `IQueryable` projection needs something an expression tree cannot carry, so it cannot be turned into a projection EF Core could translate to SQL.

## Cause

A projection property depends on a statement-bodied step - a `Use` converter or user mapping, polymorphic dispatch, an enum-to-enum conversion, or another construct that has no expression-tree form.

## How to fix it

Use a normal partial mapping method instead of a projection for that mapping, or simplify the mapping so every member is a plain assignment, a flatten path, a nested projection, or a collection projection.
