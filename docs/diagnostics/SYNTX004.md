# SYNTX004: Target member has no source

**Severity:** Warning

A member on the target type has nothing feeding it, so the generated code leaves it at its default value. The severity is configurable with `[Mapper(UnmappedTargetMember = ...)]`.

## Cause

A writable target member has no same-name source member, no flatten path, and no member configuration.

## How to fix it

Map the member with `[MapProperty]` or `[MapValue]`, rename it to match a source member, or mark it `[MapperIgnoreTarget]` if leaving it unset is intended.

## Example

```csharp
[MapperIgnoreTarget(nameof(OrderDto.ExportedAt))]
public partial OrderDto ToDto(Order order);
```
