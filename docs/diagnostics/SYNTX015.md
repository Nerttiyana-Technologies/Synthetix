# SYNTX015: Converter method not found

**Severity:** Error

A method named by a `Use` setting or a `[UserMapping]` attribute could not be found, or its signature does not accept the source value and return the target value.

## Cause

The named converter method is missing, or it does not take the source type and return the target type.

## How to fix it

Add the method, correct its name, or fix its parameter and return types so it converts from the source type to the target type.

## Example

```csharp
[MapProperty("Total", "DisplayTotal", Use = nameof(Format))]
public partial OrderDto ToDto(Order order);

// The converter must accept decimal and return string.
private static string Format(decimal value) => value.ToString("C");
```
