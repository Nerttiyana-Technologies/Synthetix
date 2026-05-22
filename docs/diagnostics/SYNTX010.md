# SYNTX010: Member type mismatch

**Severity:** Error

A source member and the target member it maps to have types that do not line up, and there is no implicit conversion and no converter.

## Cause

The two member types differ, no built-in conversion exists between them, and no `Use` converter or user mapping was supplied.

## How to fix it

Supply a `Use` converter on `[MapProperty]`, add a user mapping method between the two types, or - for string and primitive pairs - enable `[Mapper(EnableStringConversions = true)]`.

## Example

```csharp
[MapProperty("Total", "DisplayTotal", Use = nameof(Format))]
public partial OrderDto ToDto(Order order);

private static string Format(decimal value) => value.ToString("C");
```
