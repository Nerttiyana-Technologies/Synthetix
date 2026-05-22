# SYNTX005: Source member is unused

**Severity:** Info

A member on the source type is never read by any mapping. This is reported only when you opt in with `[Mapper(UnmappedSourceMember = ...)]`; it is silent by default.

## Cause

A readable source member is not used by any mapping, and the mapper has raised the severity of unused source members above `Ignore`.

## How to fix it

Map the member onto a target member, or mark it `[MapperIgnoreSource]` to state that not reading it is intentional.

## Example

```csharp
[MapperIgnoreSource(nameof(Order.InternalNotes))]
public partial OrderDto ToDto(Order order);
```
