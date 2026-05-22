# SYNTX024: Async mapping in a synchronous method

**Severity:** Error

A synchronous mapping method needs an asynchronous conversion, but a synchronous method cannot `await` anything.

## Cause

A mapping resolved to an async user mapping (one returning `Task<T>` or `ValueTask<T>`), but the mapping method itself is synchronous.

## How to fix it

Change the mapping method to return `Task<T>` or `ValueTask<T>` so it can await the conversion, or supply a synchronous converter instead.

## Example

```csharp
// Make the method async so it can await the conversion.
public partial Task<OrderDto> ToDtoAsync(Order order);
```
