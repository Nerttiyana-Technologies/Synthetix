# SYNTX001: Mapper class must be partial

**Severity:** Error

A class or struct marked with `[Mapper]` must be declared `partial`. Synthetix generates the bodies of your mapping methods into a second part of the same type, and that is only possible when the type is partial.

## Cause

The `[Mapper]` attribute is on a type that is not declared `partial`.

## How to fix it

Add the `partial` keyword to the type declaration.

## Example

```csharp
// Reports SYNTX001
[Mapper]
public class OrderMapper
{
    public partial OrderDto ToDto(Order order);
}

// Fixed
[Mapper]
public partial class OrderMapper
{
    public partial OrderDto ToDto(Order order);
}
```
