# SYNTX006: Target member not found

**Severity:** Error

A configuration attribute names a target member that does not exist on the target type.

## Cause

The target name in a `[MapProperty]`, `[MapValue]`, or `[MapperIgnoreTarget]` attribute does not match any member of the target.

## How to fix it

Correct the spelling of the target member name. Using `nameof(...)` instead of a string literal lets the compiler catch this for you.

## Example

```csharp
// Reports SYNTX006
[MapProperty("Id", "DoesNotExist")]
public partial OrderDto ToDto(Order order);
```
