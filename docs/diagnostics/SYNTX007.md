# SYNTX007: Source path not found

**Severity:** Error

A configuration attribute names a source member or dotted path that Synthetix cannot resolve on the source type.

## Cause

The source path in a `[MapProperty]` or `[MapperIgnoreSource]` attribute does not point at a readable member - a name is misspelled, or a step on a dotted path does not exist.

## How to fix it

Correct the path so every step names a readable member of the type before it.

## Example

```csharp
// Reports SYNTX007
[MapProperty("Customer.MissingName", "CustomerName")]
public partial OrderDto ToDto(Order order);
```
