# SYNTX020: Polymorphic mapping is not exhaustive

**Severity:** Error

A polymorphic mapping does not have a `[MapDerivedType]` entry for every concrete subtype of its source type, so some subtype would be mapped as its base type by accident.

## Cause

A mapping method with `[MapDerivedType]` entries is missing one for a discovered derived type.

## How to fix it

Add a `[MapDerivedType]` entry for every concrete subtype of the source type.

## Example

```csharp
[MapDerivedType(typeof(Dog), typeof(DogDto))]
[MapDerivedType(typeof(Cat), typeof(CatDto))]
public partial AnimalDto ToDto(Animal animal);
```
