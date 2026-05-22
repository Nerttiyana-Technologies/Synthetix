# SYNTX011: No usable constructor

**Severity:** Error

The target type has no accessible constructor that Synthetix can call and satisfy.

## Cause

Every constructor on the target is inaccessible, or every constructor has a parameter that cannot be filled from the source.

## How to fix it

Add an accessible parameterless constructor, or a constructor whose parameters all match source members by name.

## Example

```csharp
// Reports SYNTX011 - the only constructor is private
public class OrderDto
{
    private OrderDto() { }
    public int Id { get; set; }
}
```
