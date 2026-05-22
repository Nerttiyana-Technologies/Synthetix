# SYNTX008: Ambiguous flattening

**Severity:** Error

More than one source path folds down to the same target member name, so Synthetix cannot tell which one you meant.

## Cause

The flattening convention found two or more source paths that both resolve to a target member.

## How to fix it

Add an explicit `[MapProperty]` for that target member to choose the path you want.

## Example

```csharp
// Both Order.LineTotal and OrderLine.Total fold to OrderLineTotal.
[MapProperty("OrderLine.Total", "OrderLineTotal")]
public partial OrderDto ToDto(Order order);
```
