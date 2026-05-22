# SYNTX002: Mapping method must be partial

**Severity:** Error

A mapping method must be declared `partial` and have no body. Synthetix writes the body for you; a method that already has one, or that is not partial, is not something the generator can implement.

## Cause

A method carrying mapping attributes (such as `[MapProperty]`) is not `partial`, or it has a body.

## How to fix it

Declare the method `partial` and remove its body, or move the mapping attributes onto a real partial mapping method.

## Example

```csharp
// Reports SYNTX002
[MapProperty("Total", "DisplayTotal")]
public OrderDto ToDto(Order order) => new OrderDto();

// Fixed
[MapProperty("Total", "DisplayTotal")]
public partial OrderDto ToDto(Order order);
```
