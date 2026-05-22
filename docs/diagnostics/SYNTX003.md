# SYNTX003: No mapping could be created

**Severity:** Error

Synthetix could not build a mapping between the two types at all. This happens when one side is not a class, struct, or record it can look inside - for example a primitive type or an interface.

## Cause

The source or target of a mapping method is not a mappable complex type.

## How to fix it

Make both the source and the target a class, struct, or record. To convert to or from a simple value, use a `Use` converter or a user mapping method instead of a partial mapping method.

## Example

```csharp
// Reports SYNTX003 - the source is a primitive
public partial OrderDto ToDto(int id);
```
