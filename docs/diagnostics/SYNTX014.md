# SYNTX014: Conflicting configuration

**Severity:** Error

Two configuration attributes of equal precedence both try to set the same target member, so Synthetix cannot tell which one wins.

## Cause

A target member is named by more than one attribute of the same kind - for example two `[MapProperty]` attributes.

## How to fix it

Remove one of the conflicting attributes so each target member is configured at most once.

## Example

```csharp
// Reports SYNTX014 - X is configured twice
[MapProperty("A", "X")]
[MapProperty("B", "X")]
public partial OrderDto ToDto(Order order);
```
