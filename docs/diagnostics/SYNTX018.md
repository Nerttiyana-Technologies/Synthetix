# SYNTX018: Collection element cannot be mapped

**Severity:** Error

A collection member could not be mapped because its element type cannot be converted to the target element type.

## Cause

The source and target collections hold elements that have no built-in conversion and no mapping between them.

## How to fix it

Add a sibling mapping method or a user mapping between the two element types so Synthetix can convert each element.

## Example

```csharp
public partial CartDto ToDto(Cart cart);

// The element mapping Synthetix needs for List<Item> -> List<ItemDto>.
public partial ItemDto ToDto(Item item);
```
